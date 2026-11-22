// Small helpers called from Blazor.
window.konjugieren = {
    // Scrolls a #fragment target into view below the sticky top bar (see scroll-margin-top in app.css).
    scrollToId(id) {
        const el = document.getElementById(decodeURIComponent(id));
        if (el) el.scrollIntoView({ block: "start" });
    },
};
