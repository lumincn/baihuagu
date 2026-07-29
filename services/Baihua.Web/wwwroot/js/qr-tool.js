/**
 * 二维码工具 - JS 辅助函数
 */
window.generateQRCode = function (container, text) {
    if (!container) return;
    container.innerHTML = '';
    try {
        new QRCode(container, {
            text: text,
            width: 256,
            height: 256,
            colorDark: '#000000',
            colorLight: '#ffffff',
            correctLevel: QRCode.CorrectLevel.M
        });
    } catch (e) {
        console.error('QRCode generation failed:', e);
        container.innerHTML = '<div style="color:red;padding:1rem;">生成二维码失败</div>';
    }
};

/** 紧凑版二维码（180x180），用于节省空间的场景 */
window.generateCompactQRCode = function (container, text) {
    if (!container) return;
    container.innerHTML = '';
    try {
        new QRCode(container, {
            text: text,
            width: 180,
            height: 180,
            colorDark: '#000000',
            colorLight: '#ffffff',
            correctLevel: QRCode.CorrectLevel.M
        });
    } catch (e) {
        console.error('Compact QRCode generation failed:', e);
        container.innerHTML = '<div style="color:red;padding:1rem;">生成二维码失败</div>';
    }
};
