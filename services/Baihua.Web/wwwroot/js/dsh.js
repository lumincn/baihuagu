// DSH 桥接页面（/dsh）辅助脚本
// 滚动到底部：Blazor 端在消息更新后调用
window.scrollToBottom = (element) => {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
};
