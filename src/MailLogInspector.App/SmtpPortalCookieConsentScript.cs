namespace MailLogInspector.App;

public static class SmtpPortalCookieConsentScript
{
    public const string RejectAll =
        """
        (() => {
            const normalize = value =>
                String(value || '').replace(/\s+/g, ' ').trim().toLowerCase();
            const rejectLabels = new Set([
                'Reject All',
                'Reject All Cookies',
                'Decline All',
                'Deny All'
            ].map(normalize));
            const controls = [...document.querySelectorAll(
                'button, [role="button"], a, input[type="button"], input[type="submit"]')];
            const reject = controls.find(control =>
                rejectLabels.has(normalize(
                    control.innerText ||
                    control.value ||
                    control.getAttribute('aria-label') ||
                    control.textContent)));
            if (!reject) return false;
            reject.click();
            return true;
        })()
        """;
}
