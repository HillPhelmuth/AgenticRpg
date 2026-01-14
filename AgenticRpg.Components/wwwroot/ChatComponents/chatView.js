// ChatView scroll tracking and management

export function initializeScrollTracking(messagesContainer, dotNetHelper) {
    if (!messagesContainer) return;

    const checkScrollPosition = () => {
        const isAtBottom = 
            messagesContainer.scrollHeight - messagesContainer.scrollTop <= 
            messagesContainer.clientHeight + 50; // 50px threshold
        
        dotNetHelper.invokeMethodAsync('UpdateScrollPosition', isAtBottom);
    };

    messagesContainer.addEventListener('scroll', checkScrollPosition);
    
    // Initial check
    checkScrollPosition();

    // Return cleanup function
    return () => {
        messagesContainer.removeEventListener('scroll', checkScrollPosition);
    };
}

export function scrollToBottom(messagesContainer) {
    if (!messagesContainer) return;

    messagesContainer.scrollTo({
        top: messagesContainer.scrollHeight,
        behavior: 'smooth'
    });
}
