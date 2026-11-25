const UPDATE_SCROLL = 'UpdateScrollPosition';
const NEW_CONTENT = 'NotifyNewContent';
const scrollHandlers = new WeakMap();
export function initializeScrollTracking(container, helper) {
    if (!container) return;
    disposeScrollTracking(container);
    const notify = async (method, ...args) => { try { await helper.invokeMethodAsync(method, ...args); } catch { /* disposed circuit */ } };
    let pinned = container.scrollHeight - container.scrollTop <= container.clientHeight + 50;
    const report = () => {
        pinned = container.scrollHeight - container.scrollTop <= container.clientHeight + 50;
        void notify(UPDATE_SCROLL, pinned);
    };
    const observer = new MutationObserver(() => {
        if (pinned) { container.scrollTop = container.scrollHeight; report(); }
        else void notify(NEW_CONTENT);
    });
    observer.observe(container, { childList: true, subtree: true, characterData: true });
    const resize = new ResizeObserver(() => { if (pinned) container.scrollTop = container.scrollHeight; });
    resize.observe(container);
    container.addEventListener('scroll', report, { passive: true });
    scrollHandlers.set(container, { report, observer, resize });
    container.scrollTop = container.scrollHeight;
    report();
}
export function scrollToBottom(container) {
    if (container) container.scrollTo({ top: container.scrollHeight, behavior: 'smooth' });
}
export function disposeScrollTracking(container) {
    const tracking = scrollHandlers.get(container);
    if (tracking) { container.removeEventListener('scroll', tracking.report); tracking.observer.disconnect(); tracking.resize.disconnect(); }
    scrollHandlers.delete(container);
}
