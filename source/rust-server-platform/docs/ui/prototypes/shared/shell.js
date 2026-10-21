/* Shared shell: all prototype pages use the same markup. */
(()=>{
  const script=document.currentScript;
  if(!script)return;
  const part=script.dataset.part;
  const page=(script.dataset.page||'').toLowerCase();
  const header=`<div class="brand"><div class="rust-logo-slot" title="Rust game — official site icon"><div class="rust-logo-tile"><img src="https://rust.facepunch.com/favicon.ico" width="36" height="36" alt="Rust game logo" onerror="this.hidden=true;this.nextElementSibling.hidden=false"><span class="rust-logo-fallback" hidden>R</span></div></div><strong>RUSTSERVER</strong><span class="muted">/ Control Panel</span><span class="muted right" style="font-size:11px">${page.toUpperCase()} · PROTOTYPE · SIMULATED DATA</span></div>`;
  const links=[['Overview','▦'],['Server','▣'],['World','◈'],['Plugins','⬡']];
  const nav='<nav class="nav"><div class="overline">PLATFORM</div>'+links.map(([name,symbol])=>'<button type="button" '+(name.toLowerCase()===page?'class="selected" ':'')+'data-shell-nav="'+name+'">'+symbol+'&nbsp; '+name+'</button>').join('')+'</nav>';
  script.insertAdjacentHTML('beforebegin',part==='header'?header:nav);
  script.remove();
  if(part==='nav')document.querySelectorAll('[data-shell-nav]').forEach(b=>b.addEventListener('click',()=>{
    if(b.dataset.shellNav.toLowerCase()===page)return;
    const routes={Overview:'../overview/prototype-v5.html',Server:'../server/prototype-v7.html',World:'../world/prototype-v1.html',Plugins:'../plugins-management/prototype-v15.html'};
    if(routes[b.dataset.shellNav])location.href=routes[b.dataset.shellNav];
  }));
})();
