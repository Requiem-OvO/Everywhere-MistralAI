namespace Everywhere.Web;

internal static partial class WebExtractionScripts
{
    public const string BrowserHardening =
        """
        () => {
            window.console.log = () => {};
            window.console.info = () => {};
            window.console.warn = () => {};
            window.console.error = () => {};
            window.console.debug = () => {};
            window.console.dir = () => {};
            window.console.dirxml = () => {};
            window.console.trace = () => {};
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });
        }
        """;

    public const string AutoScroll =
        """
        async () => {
            let lastHeight = document.body?.scrollHeight ?? 0;
            let iterations = 0;
            while (iterations < 3) {
                window.scrollBy(0, 1000);
                await new Promise(r => setTimeout(r, 800));

                const newHeight = document.body?.scrollHeight ?? 0;
                if (newHeight === lastHeight) {
                    break;
                }
                lastHeight = newHeight;
                iterations++;
            }
        }
        """;

    public const string ReadBodyText =
        """
        () => document.body?.textContent || document.documentElement?.textContent || ''
        """;

    public const string ReadDocumentContentType =
        """
        () => document.contentType || ''
        """;

    public const string ExtractWithReadability =
        """
        () => {
            var documentClone = document.cloneNode(true);
            documentClone.querySelectorAll('svg').forEach(el => el.remove());
            documentClone.querySelectorAll('img').forEach(el => {
                if (el.src && el.src.startsWith('data:image/')) {
                    el.remove();
                }
            });
            documentClone.querySelectorAll('a').forEach(el => {
                if (el.href && el.href.length > 300) {
                    el.href = el.href.substring(0, 300) + '...';
                }
            });
            var reader = new Readability(documentClone, {
                keepClasses: true,
                charThreshold: 100,
                classesToPreserve: ['markdown-body', 'highlight', 'code', 'table', 'comment', 'reply']
            });
            return reader.parse();
        }
        """;

    public const string ExtractDomMainElement =
        """
        () => {
            const selectors = [
                'article.markdown-body',
                '.markdown-body',
                '#readme',
                '[data-testid=readme]',
                '[itemprop=articleBody]',
                '.learn-article',
                '.doc-content',
                '.docs-content',
                '.theme-doc-markdown',
                '.markdown-section',
                'main article',
                '.main-content',
                '#main-content',
                '.article-body',
                '.post-content',
                '.entry-content',
                '.content',
                '[role=main]',
                'article',
                'main'
            ];

            for (const selector of selectors) {
                const candidates = Array.from(document.querySelectorAll(selector))
                    .map(element => {
                        const clone = cleanElementForExtraction(element);
                        return {
                            element,
                            html: clone.innerHTML || '',
                            text: clone.textContent?.trim() || ''
                        };
                    })
                    .filter(candidate => candidate.text.length > 0)
                    .sort((left, right) => right.text.length - left.text.length);

                const candidate = candidates[0];
                if (candidate) {
                    return {
                        title: document.title || null,
                        html: candidate.html,
                        text: candidate.text
                    };
                }
            }

            return null;

            function cleanElementForExtraction(element) {
                const clone = element.cloneNode(true);
                clone.querySelectorAll('script,style,noscript,nav,header,footer,aside,svg').forEach(el => el.remove());
                clone.querySelectorAll('img').forEach(el => {
                    if (el.src && el.src.startsWith('data:image/')) {
                        el.remove();
                    }
                });
                clone.querySelectorAll('a').forEach(el => {
                    if (el.href && el.href.length > 300) {
                        el.href = el.href.substring(0, 300) + '...';
                    }
                });
                return clone;
            }
        }
        """;

    public const string ExtractCleanedBody =
        """
        () => {
            const root = document.body || document.documentElement;
            if (!root) {
                return null;
            }

            const clone = root.cloneNode(true);
            clone.querySelectorAll('script,style,noscript,nav,header,footer,aside,svg').forEach(el => el.remove());
            clone.querySelectorAll('img').forEach(el => {
                if (el.src && el.src.startsWith('data:image/')) {
                    el.remove();
                }
            });
            clone.querySelectorAll('a').forEach(el => {
                if (el.href && el.href.length > 300) {
                    el.href = el.href.substring(0, 300) + '...';
                }
            });

            return {
                title: document.title || null,
                html: clone.innerHTML || '',
                text: clone.textContent?.trim() || ''
            };
        }
        """;
}