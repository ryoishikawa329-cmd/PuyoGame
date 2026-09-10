// Unity の WebGL 音声は WEBAudio.audioContext を通る。
// iOS Safari は利用者が画面を触るまでこれを suspended のままにするので、
// タップの処理の中から resume() を呼べるようにしておく。
//
// 戻り値: 1=鳴らせる 0=まだ止まっている -1=Unityの音声機構が見つからない
mergeInto(LibraryManager.library, {

  // index.html から呼べるよう、resume する関数を window に出しておく。
  // ブラウザは「利用者の操作の中で呼ばれたか」を見るため、
  // Unity の Update から呼ぶだけでは解除されないことがある。
  PuyoAudioBridgeInit: function () {
    if (typeof window === 'undefined') return;
    window.puyoResumeAudio = function () {
      try {
        if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) return -1;
        var ctx = WEBAudio.audioContext;
        if (ctx.state !== 'running') ctx.resume();
        return ctx.state === 'running' ? 1 : 0;
      } catch (e) {
        return -1;
      }
    };
  },

  PuyoAudioResume: function () {
    try {
      if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) return -1;
      var ctx = WEBAudio.audioContext;
      if (ctx.state !== 'running') ctx.resume();
      return ctx.state === 'running' ? 1 : 0;
    } catch (e) {
      return -1;
    }
  },

  PuyoAudioIsRunning: function () {
    try {
      if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) return -1;
      return WEBAudio.audioContext.state === 'running' ? 1 : 0;
    } catch (e) {
      return -1;
    }
  }
});
