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

/**
 * 紧凑版二维码（180x180），用于节省空间的场景。
 * 兼容三种入参：元素 id 字符串 / 已解析的 HTMLElement / Blazor ElementReference。
 * Blazor Server 时序问题：条件块内的容器可能尚未渲染（ElementReference 未解析），
 * 传 id 时内部轮询等待元素出现（最多 2s），彻底避免 "appendChild is not a function" 崩溃。
 * 返回 Promise<boolean>（生成成功与否），Blazor 侧可用 InvokeAsync<bool> 接收。
 */
window.generateCompactQRCode = function (containerOrId, text) {
    return new Promise((resolve) => {
        let el = resolveQrElement(containerOrId);
        if (el) {
            resolve(renderCompactQR(el, text));
            return;
        }
        // 容器尚未渲染，等待重试
        let tries = 0;
        const timer = setInterval(() => {
            tries++;
            el = resolveQrElement(containerOrId);
            if (el || tries >= 20) {
                clearInterval(timer);
                if (el) {
                    resolve(renderCompactQR(el, text));
                } else {
                    console.error('QR container not found:', containerOrId);
                    resolve(false);
                }
            }
        }, 100);
    });
};

function resolveQrElement(refOrId) {
    if (typeof refOrId === 'string') {
        return document.getElementById(refOrId);
    }
    if (refOrId && typeof refOrId === 'object') {
        // Blazor ElementReference 已解析时是真实 DOM 元素；未解析时是 {__internalId} 占位
        if (refOrId instanceof HTMLElement) return refOrId;
        if (refOrId.__internalId !== undefined) return null;
        return null;
    }
    return null;
}

function renderCompactQR(el, text) {
    if (!el) return false;
    el.innerHTML = '';
    try {
        new QRCode(el, {
            text: text,
            width: 180,
            height: 180,
            colorDark: '#000000',
            colorLight: '#ffffff',
            correctLevel: QRCode.CorrectLevel.M
        });
        return true;
    } catch (e) {
        console.error('Compact QRCode generation failed:', e);
        const tooLong = e && e.message && String(e.message).includes('overflow');
        el.innerHTML = tooLong
            ? '<div style="color:red;padding:1rem;">内容过长，二维码无法生成，请缩短后重试</div>'
            : '<div style="color:red;padding:1rem;">生成二维码失败</div>';
        return false;
    }
}
