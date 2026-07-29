/**
 * 为 Markdown 渲染后的代码块添加复制按钮
 */
function addCopyButtonsToCodeBlocks() {
    document.querySelectorAll('.message-text pre').forEach(function(pre) {
        // 避免重复添加
        if (pre.querySelector('.code-copy-btn')) return;

        var btn = document.createElement('button');
        btn.className = 'code-copy-btn';
        btn.innerHTML = '📋 复制';
        btn.title = '复制代码';

        btn.addEventListener('click', function() {
            var code = pre.querySelector('code');
            var text = code ? code.innerText : pre.innerText;
            navigator.clipboard.writeText(text).then(function() {
                btn.innerHTML = '✅ 已复制';
                btn.classList.add('copied');
                setTimeout(function() {
                    btn.innerHTML = '📋 复制';
                    btn.classList.remove('copied');
                }, 2000);
            }).catch(function() {
                btn.innerHTML = '❌ 失败';
                setTimeout(function() {
                    btn.innerHTML = '📋 复制';
                }, 2000);
            });
        });

        pre.appendChild(btn);
    });
}

// 暴露给 Blazor 调用
window.addCopyButtonsToCodeBlocks = addCopyButtonsToCodeBlocks;
