let speechSynthesis = window.speechSynthesis;
let currentUtterance = null;
let dotNetRef = null;
let resumeTimer = null;

window.speakText = function(text, dotNetObjRef, seq) {
    if (speechSynthesis) {
        speechSynthesis.cancel();
    }
    if (resumeTimer) {
        clearInterval(resumeTimer);
        resumeTimer = null;
    }
    currentUtterance = null;

    if (!speechSynthesis) {
        console.warn('浏览器不支持语音合成');
        return;
    }

    if (dotNetObjRef) {
        if (dotNetRef && dotNetRef !== dotNetObjRef) {
            try { dotNetRef.dispose(); } catch (e) {}
        }
        dotNetRef = dotNetObjRef;
    }

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = 'zh-CN';
    utterance.rate = 1.0;
    utterance.pitch = 1.0;

    const mySeq = seq;

    utterance.onstart = function() {
        resumeTimer = setInterval(function() {
            if (speechSynthesis && speechSynthesis.speaking && speechSynthesis.paused) {
                speechSynthesis.resume();
            }
        }, 10000);
    };

    utterance.onend = function() {
        currentUtterance = null;
        if (resumeTimer) {
            clearInterval(resumeTimer);
            resumeTimer = null;
        }
        if (dotNetRef) {
            setTimeout(function() {
                try {
                    dotNetRef.invokeMethodAsync('OnSpeechEnded', mySeq);
                } catch (e) {
                    console.warn('通知播放结束失败:', e);
                }
            }, 100);
        }
    };

    utterance.onerror = function(e) {
        currentUtterance = null;
        if (resumeTimer) {
            clearInterval(resumeTimer);
            resumeTimer = null;
        }
        if (e && e.error === 'canceled') return;
        if (dotNetRef) {
            setTimeout(function() {
                try {
                    dotNetRef.invokeMethodAsync('OnSpeechEnded', mySeq);
                } catch (ex) {
                    console.warn('通知播放错误失败:', ex);
                }
            }, 100);
        }
    };

    currentUtterance = utterance;
    speechSynthesis.speak(utterance);
};

window.stopSpeaking = function() {
    if (speechSynthesis) {
        speechSynthesis.cancel();
    }
    if (resumeTimer) {
        clearInterval(resumeTimer);
        resumeTimer = null;
    }
    currentUtterance = null;
};

window.isSpeaking = function() {
    return speechSynthesis && speechSynthesis.speaking;
};
