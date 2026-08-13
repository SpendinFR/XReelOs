namespace MLOmega.XR.UI
{
    /// <summary>
    /// JavaScript injected only after an explicit press on the Lab VR control.
    /// It exposes the decoded HTML5 video frame before a site's WebGL/WebXR
    /// preview reprojects it, while preserving enough DOM state to restore the
    /// original player on exit.
    /// </summary>
    internal static class XrLabWebVrScript
    {
        public const string ProbeAndExposeRawVideo =
            "try{(function(){" +
            "var vw=innerWidth||document.documentElement.clientWidth||1;" +
            "var vh=innerHeight||document.documentElement.clientHeight||1;" +
            "if(window.__xreelVrRestore){try{window.__xreelVrRestore();}catch(oldErr){}}" +
            "var videos=[];var seen=[];" +
            "function add(v){if(v&&seen.indexOf(v)<0){seen.push(v);videos.push(v);}}" +
            "function scan(root){if(!root||!root.querySelectorAll)return;" +
            "var direct=root.querySelectorAll('video');for(var i=0;i<direct.length;i++)add(direct[i]);" +
            "var nodes=root.querySelectorAll('*');for(var j=0;j<nodes.length;j++){" +
            "var shadow=nodes[j].shadowRoot;if(shadow)scan(shadow);}}" +
            "scan(document);" +
            "var media=null,best=-1;for(var k=0;k<videos.length;k++){var v=videos[k];" +
            "var r=v.getBoundingClientRect();var area=Math.max(1,r.width*r.height);" +
            "var visible=r.width>16&&r.height>16&&r.bottom>0&&r.right>0&&r.top<vh&&r.left<vw;" +
            "var score=area+(visible?1e8:0)+(!v.paused?1e10:0)+(v.readyState>=2?1e9:0);" +
            "if(score>best){best=score;media=v;}}" +
            "if(!media){var resources=[];try{resources=performance.getEntriesByType('resource')" +
            ".map(function(x){return x.name;}).filter(function(x){return /\\.(mp4|m3u8|mpd)(\\?|$)/i.test(x);})" +
            ".slice(-8);}catch(perfErr){}" +
            "tlab.postResult(xrResultId,JSON.stringify({ok:false,detail:'no-html5-video'," +
            "hint:resources.join(' ')}));return;}" +
            "if(media.paused){try{var promise=media.play();" +
            "if(promise&&promise.catch)promise.catch(function(){});}catch(playErr){}}" +
            "var parent=media.parentNode,next=media.nextSibling;" +
            "var oldStyle=media.getAttribute('style');var oldControls=media.controls;" +
            "var oldPlaysInline=media.playsInline;var oldWebkit=media.getAttribute('webkit-playsinline');" +
            "var overlay=document.createElement('div');overlay.id='__xreel_vr_raw_host';" +
            "overlay.style.cssText='position:fixed!important;inset:0!important;z-index:2147483646!important;' +" +
            "'background:#000!important;overflow:hidden!important;pointer-events:none!important;';" +
            "(document.documentElement||document.body).appendChild(overlay);overlay.appendChild(media);" +
            "var mw=media.videoWidth||0,mh=media.videoHeight||0;var prior=media.getBoundingClientRect();" +
            "var ar=(mw>0&&mh>0)?mw/mh:(prior.height>0?prior.width/prior.height:2);" +
            "if(!isFinite(ar)||ar<=0)ar=2;var w=vw,h=vw/ar;" +
            "if(h>vh){h=vh;w=vh*ar;}var x=(vw-w)*.5,y=(vh-h)*.5;" +
            "media.controls=false;media.playsInline=true;media.setAttribute('playsinline','');" +
            "media.setAttribute('webkit-playsinline','');" +
            "media.style.cssText='position:absolute!important;display:block!important;opacity:1!important;' +" +
            "'visibility:visible!important;transform:none!important;max-width:none!important;max-height:none!important;' +" +
            "'object-fit:fill!important;pointer-events:none!important;background:#000!important;' +" +
            "'left:'+x+'px!important;top:'+y+'px!important;width:'+w+'px!important;height:'+h+'px!important;';" +
            "window.__xreelVrRestore=function(){try{" +
            "if(parent){if(next&&next.parentNode===parent)parent.insertBefore(media,next);else parent.appendChild(media);}" +
            "if(oldStyle===null)media.removeAttribute('style');else media.setAttribute('style',oldStyle);" +
            "media.controls=oldControls;media.playsInline=oldPlaysInline;" +
            "if(oldWebkit===null)media.removeAttribute('webkit-playsinline');else media.setAttribute('webkit-playsinline',oldWebkit);" +
            "if(overlay&&overlay.parentNode)overlay.parentNode.removeChild(overlay);" +
            "}finally{window.__xreelVrRestore=null;}};" +
            "var src=String(media.currentSrc||media.src||'');" +
            "var attrs=' title='+String(media.getAttribute('title')||'')+" +
            "' projection='+String(media.getAttribute('data-projection')||'')+" +
            "' stereo_mode='+String(media.getAttribute('stereo-mode')||'')+" +
            "' class='+String(media.className||'');" +
            "var hint=(String(location.hostname||'')+' '+String(location.pathname||'')+' '+src+attrs).slice(0,640);" +
            "tlab.postResult(xrResultId,JSON.stringify({ok:true,x:x,y:y,w:w,h:h,vw:vw,vh:vh," +
            "videoWidth:mw,videoHeight:mh,hint:hint,kind:'RAW_HTML5_VIDEO'," +
            "detail:'videos='+videos.length+',video='+mw+'x'+mh+',src='+(src?'url':'blob-or-mse')}));" +
            "})();}catch(err){tlab.postResult(xrResultId,JSON.stringify({ok:false," +
            "detail:'js:'+String(err&&err.name)+':'+String(err&&err.message)}));}";

        public const string RestoreRawVideo =
            "try{if(window.__xreelVrRestore)window.__xreelVrRestore();}catch(err){}";
    }
}
