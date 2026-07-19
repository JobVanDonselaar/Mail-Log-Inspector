using System.Text.Json;

namespace MailLogInspector.App;

public static class SmtpPortalReportDomScripts
{
    public const string ReadFirstPageReports =
        """
        (() => {
            const normalize = value => String(value || '').replace(/\s+/g, ' ').trim();
            const statusPattern = /\b(Ready|Processing|Failed)\b/i;
            const controlsIn = container =>
                [...container.querySelectorAll('button, a, [role="button"]')]
                    .filter(control => control.offsetParent !== null);
            const findReportContainer = node => {
                let current = node;
                for (let depth = 0; current && depth < 8; depth++, current = current.parentElement) {
                    const text = normalize(current.innerText || current.textContent);
                    const controls = controlsIn(current);
                    if (statusPattern.test(text) && controls.length >= 1 && controls.length <= 4) {
                        return current;
                    }
                }

                return node.closest('tr, [role="row"], li, .ant-list-item') || node.parentElement;
            };
            const nameNodes = [...document.querySelectorAll('body *')]
                .filter(node => {
                    const text = normalize(node.textContent);
                    if (!text.startsWith('NextGen_')) return false;
                    return ![...node.children]
                        .some(child => normalize(child.textContent).startsWith('NextGen_'));
                });
            const result = [];
            const seen = new Set();
            nameNodes.forEach((node, index) => {
                const name = normalize(node.textContent);
                if (seen.has(name)) return;
                const container = findReportContainer(node);
                const containerText = normalize(container && (container.innerText || container.textContent));
                const statusMatch = containerText.match(statusPattern);
                if (!statusMatch) return;
                seen.add(name);
                result.push({ name, status: statusMatch[1], rowKey: String(index) });
            });
            return result.slice(0, 100);
        })()
        """;

    public static string BuildDownloadClick(string reportName)
    {
        string reportNameJson = JsonSerializer.Serialize(reportName);
        return
            $$"""
            (() => {
                const reportName = {{reportNameJson}};
                const normalize = value => String(value || '').replace(/\s+/g, ' ').trim();
                const statusPattern = /\b(Ready|Processing|Failed)\b/i;
                const controlsIn = container =>
                    [...container.querySelectorAll('button, a, [role="button"]')]
                        .filter(control => control.offsetParent !== null);
                const findReportContainer = node => {
                    let current = node;
                    for (let depth = 0; current && depth < 8; depth++, current = current.parentElement) {
                        const text = normalize(current.innerText || current.textContent);
                        const controls = controlsIn(current);
                        if (statusPattern.test(text) && controls.length >= 1 && controls.length <= 4) {
                            return current;
                        }
                    }

                    return node.closest('tr, [role="row"], li, .ant-list-item') || node.parentElement;
                };
                const nameNode = [...document.querySelectorAll('body *')]
                    .find(node => {
                        if (normalize(node.textContent) !== reportName) return false;
                        return ![...node.children]
                            .some(child => normalize(child.textContent) === reportName);
                    });
                if (!nameNode) return false;
                const container = findReportContainer(nameNode);
                if (!container) return false;
                const controls = controlsIn(container);
                const download = controls.find(control =>
                    /download/i.test([
                        control.getAttribute('aria-label') || '',
                        control.getAttribute('title') || '',
                        control.getAttribute('download') || '',
                        control.getAttribute('href') || '',
                        control.textContent || ''
                    ].join(' '))) || (controls.length === 1 ? controls[0] : null);
                if (!download) return false;
                download.scrollIntoView({ block: 'center', inline: 'nearest' });
                download.click();
                return true;
            })()
            """;
    }
}
