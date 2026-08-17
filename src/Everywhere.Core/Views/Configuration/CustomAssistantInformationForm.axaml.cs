using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Everywhere.AI;
using SkiaSharp;
using SKBlendMode = SkiaSharp.SKBlendMode;
using SKPaint = SkiaSharp.SKPaint;
using SKRect = SkiaSharp.SKRect;

namespace Everywhere.Views;

/// <summary>
/// A form for configuring CustomAssistant information (Icon, Name and Description).
/// </summary>
public class CustomAssistantInformationForm : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="CustomAssistant"/> property.
    /// </summary>
    public static readonly StyledProperty<CustomAssistant?> CustomAssistantProperty =
        AvaloniaProperty.Register<CustomAssistantInformationForm, CustomAssistant?>(nameof(CustomAssistant));

    /// <summary>
    /// Gets or sets the CustomAssistant to configure.
    /// </summary>
    public CustomAssistant? CustomAssistant
    {
        get => GetValue(CustomAssistantProperty);
        set => SetValue(CustomAssistantProperty, value);
    }
}

public sealed class RadialGlow : Control
{
    public static readonly StyledProperty<Color> ColorProperty = AvaloniaProperty.Register<RadialGlow, Color>(nameof(Color), Colors.Transparent);

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }


    private static readonly Lazy<SKRuntimeEffect> ShaderEffect = new(CreateEffect);

    private static SKRuntimeEffect CreateEffect()
    {
        return SKRuntimeEffect.CreateShader(ShaderSource, out var errorText) ??
            throw new InvalidOperationException("Failed to compile radial glow shader: " + errorText);
    }

    static RadialGlow()
    {
        AffectsRender<RadialGlow>(ColorProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        var color = Color;

        if (bounds.Width <= 0 || bounds.Height <= 0 || color.A == 0)
        {
            return;
        }

        context.Custom(new GlowDrawOperation(bounds, color));
    }

    private sealed class GlowDrawOperation(Rect bounds, Color color) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        private readonly Color _color = color;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                return;
            }

            using var lease = feature.Lease();

            var canvas = lease.SkCanvas;
            var width = (float)Bounds.Width;
            var height = (float)Bounds.Height;

            var save = canvas.Save();

            try
            {
                // Keep shader coordinates local to this draw operation.
                canvas.Translate((float)Bounds.X, (float)Bounds.Y);
                canvas.Scale(1.0f, 0.9f);
                canvas.ClipRect(new SKRect(0, 0, width, height));

                var matrix = canvas.TotalMatrix;
                var scaleX = MathF.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
                var scaleY = MathF.Sqrt(matrix.SkewX * matrix.SkewX + matrix.ScaleY * matrix.ScaleY);

                var effect = ShaderEffect.Value;
                using var uniforms = new SKRuntimeEffectUniforms(effect);

                // Control size in Avalonia drawing units.
                uniforms["uSize"] = new[] { width, height };
                // Control size in pixel units. This is used to compute the radial falloff.
                uniforms["uPixelScale"] = new[] { scaleX, scaleY };
                // Input color is normalized sRGB. The alpha is multiplied by MaxOpacity in the shader.
                uniforms["uColor"] = new[]
                {
                    _color.R / 255f,
                    _color.G / 255f,
                    _color.B / 255f,
                    _color.A / 255f
                };

                using var children = new SKRuntimeEffectChildren(effect);
                using var shader = effect.ToShader(uniforms, children);

                using var paint = new SKPaint();
                paint.Shader = shader;
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.IsAntialias = false;
                paint.IsDither = true;

                canvas.DrawRect(new SKRect(0, 0, width, height), paint);
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }

        public bool HitTest(Point p)
        {
            return false;
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is GlowDrawOperation operation && operation.Bounds == Bounds && operation._color == _color;
        }

        public void Dispose()
        {
        }
    }

    private const string ShaderSource =
        """
        uniform float2 uSize;
        uniform float2 uPixelScale;
        layout(color) uniform float4 uColor;
        
        float luminance(float3 c)
        {
            return dot(c, float3(0.2126, 0.7152, 0.0722));
        }
        
        float3 clampGlowColor(float3 c)
        {
            c = clamp(c, float3(0.0), float3(1.0));
        
            // These are visual limits for glow color, not physical color limits.
            // Too dark colors produce muddy glow; too bright colors produce flat neon patches.
            const float MinLuma = 0.18;
            const float MaxLuma = 0.72;
        
            float y = max(luminance(c), 1e-5);
        
            // Scale over-bright colors down while preserving hue direction.
            if (y > MaxLuma)
            {
                c *= MaxLuma / y;
            }
        
            // Lift under-bright colors. Exact hue preservation is impossible for very saturated
            // dark colors inside the RGB gamut, so we allow a small neutral lift.
            if (y < MinLuma)
            {
                c *= MinLuma / y;
        
                float maxChannel = max(max(c.r, c.g), c.b);
                if (maxChannel > 1.0)
                {
                    c /= maxChannel;
                }
        
                float y2 = luminance(c);
                float neutralLift = clamp((MinLuma - y2) / MinLuma, 0.0, 0.35);
                c = mix(c, float3(MinLuma), neutralLift);
            }
        
            return clamp(c, float3(0.0), float3(1.0));
        }

        float hash12(float2 p)
        {
            float3 p3 = fract(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float triangularNoise(float2 p)
        {
            // Triangular noise is less harsh than plain white noise.
            return hash12(p) - hash12(p + 19.19);
        }

        float smootherstep01(float x)
        {
            x = clamp(x, 0.0, 1.0);
            return x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
        }

        float4 main(float2 p)
        {
            float2 size = max(uSize, float2(1.0));

            // Pixel-space center and radius.
            // The radius is exactly half of the shortest side.
            float2 center = size * 0.5;
            float radius = min(size.x, size.y) * 0.5;

            float t = length(p - center) / max(radius, 1.0);

            // Outside the radial support, output fully transparent premultiplied black.
            // This removes the rectangular clipping edge.
            if (t >= 1.0)
            {
                return float4(0.0);
            }

            const float MaxOpacity = 0.4;

            // Larger value means a more concentrated glow.
            // Around 1.15 - 1.60 usually looks good for soft UI glow.
            const float FalloffPower = 1.25;

            // Stronger than the previous version. The previous 0.75 / 255 was too weak.
            const float AlphaDitherStrength = 4.0 / 255.0;

            // RGB dithering is applied after premultiplication to break remaining color-channel bands.
            const float RgbDitherStrength = 1.5 / 255.0;

            // Compact-support smooth falloff.
            // This reaches exactly zero at t = 1 and has smooth derivatives at both ends.
            float fade = 1.0 - smootherstep01(t);
            float3 glowColor = clampGlowColor(uColor.rgb);
            float alpha = pow(fade, FalloffPower) * MaxOpacity * uColor.a;

            // Fade dithering out near the edge so it does not create noisy speckles at the border.
            float edgeMask = 1.0 - smoothstep(0.94, 1.0, t);

            float2 pixel = p * uPixelScale;
            float noise = triangularNoise(pixel);

            // Dither alpha first.
            alpha = clamp(alpha + noise * AlphaDitherStrength * edgeMask, 0.0, 1.0);

            // Skia expects premultiplied output.
            float3 premul = uColor.rgb * alpha;

            // Dither premultiplied RGB as well. This is important because alpha-only dithering
            // can still leave visible rings after the final 8-bit framebuffer quantization.
            premul += noise * RgbDitherStrength * edgeMask;

            // Keep the premultiplied invariant: rgb must not exceed alpha.
            premul = clamp(premul, float3(0.0), float3(alpha));

            return float4(premul, alpha);
        }
        """;
}