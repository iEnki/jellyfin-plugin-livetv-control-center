(function () {
    'use strict';
    const dictionaries = window.LiveTvGroupsTranslations;
    function locale() {
        const lang = document.documentElement.lang;
        if (lang && lang !== 'auto') return lang;
        try { const user = window.ApiClient?.getCurrentUserId?.(); const saved = localStorage.getItem(user + '-language'); if (saved && saved !== 'auto') return saved; } catch (_) {}
        return navigator.language || 'en-US';
    }
    function t(key) { return dictionaries[locale().toLowerCase().split(/[-_]/)[0]]?.[key] ?? key; }
    function translate(root) {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) { const node = walker.currentNode; if (node.parentElement?.closest('script,style')) continue; const key = node.textContent.trim(); if(key) node.textContent = node.textContent.replace(key, t(key)); }
        root.querySelectorAll('[aria-label],[placeholder]').forEach(node => { for(const attr of ['aria-label','placeholder']) if(node.hasAttribute(attr)) node.setAttribute(attr,t(node.getAttribute(attr))); });
    }
    window.LiveTvGroupsI18n = { t, locale, translate };
})();
