namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants
open Types

/// <summary>
/// Provides htmlreport functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module HtmlReport =

    let private ci = CultureInfo.InvariantCulture
    let private n (x: float) =
        if Double.IsNaN x || Double.IsInfinity x then "null" else x.ToString("G6", ci)
    let private arr (xs: seq<float>) = "[" + String.Join(",", xs |> Seq.map n) + "]"
    let private sarr (xs: seq<string>) =
        "[" + String.Join(",", xs |> Seq.map (fun s -> "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]"

    let private esc (s: string) =
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

    let private css = """
:root{color-scheme:light dark}
*{box-sizing:border-box}
body{margin:0;font:14px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;
     background:var(--bg);color:var(--text-primary)}
.viz-root{
  --bg:#f5f5f3; --surface-1:#fcfcfb; --border:#e3e2de;
  --text-primary:#0b0b0b; --text-secondary:#52514e; --text-muted:#83827d;
  --s1:#2a78d6; --s2:#eb6834; --s3:#1baf7a; --s4:#eda100; --s5:#e87ba4; --s6:#008300;
  --grid:#e8e7e3; --band:#2a78d61f;
  --seq0:#cde2fb; --seq1:#9ec5f4; --seq2:#6da7ec; --seq3:#3987e5; --seq4:#256abf; --seq5:#184f95; --seq6:#0d366b;
  --div-neg:#e34948; --div-mid:#f0efec; --div-pos:#2a78d6;
  --good:#008300; --warn:#eda100; --crit:#e34948;
}
@media (prefers-color-scheme:dark){
 :root:where(:not([data-theme="light"])) .viz-root{
  --bg:#111110; --surface-1:#1a1a19; --border:#33332f;
  --text-primary:#fff; --text-secondary:#c3c2b7; --text-muted:#8f8e86;
  --s1:#3987e5; --s2:#d95926; --s3:#199e70; --s4:#c98500; --s5:#d55181; --s6:#008300;
  --grid:#2a2a27; --band:#3987e526;
  --seq0:#0d366b; --seq1:#184f95; --seq2:#256abf; --seq3:#3987e5; --seq4:#6da7ec; --seq5:#9ec5f4; --seq6:#cde2fb;
  --div-neg:#e66767; --div-mid:#383835; --div-pos:#3987e5;
 }}
:root[data-theme="dark"] .viz-root{
  --bg:#111110; --surface-1:#1a1a19; --border:#33332f;
  --text-primary:#fff; --text-secondary:#c3c2b7; --text-muted:#8f8e86;
  --s1:#3987e5; --s2:#d95926; --s3:#199e70; --s4:#c98500; --s5:#d55181; --s6:#008300;
  --grid:#2a2a27; --band:#3987e526;
  --seq0:#0d366b; --seq1:#184f95; --seq2:#256abf; --seq3:#3987e5; --seq4:#6da7ec; --seq5:#9ec5f4; --seq6:#cde2fb;
  --div-neg:#e66767; --div-mid:#383835; --div-pos:#3987e5;
}
.wrap{max-width:1180px;margin:0 auto;padding:28px 20px 64px}
header h1{font-size:22px;margin:0 0 4px}
header p{margin:0;color:var(--text-secondary)}
.toggle{position:fixed;top:14px;right:16px;border:1px solid var(--border);background:var(--surface-1);
        color:var(--text-secondary);border-radius:8px;padding:6px 10px;cursor:pointer;font-size:12px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin:22px 0 6px}
.tile{background:var(--surface-1);border:1px solid var(--border);border-radius:12px;padding:14px 16px}
.tile .lab{font-size:11px;letter-spacing:.04em;text-transform:uppercase;color:var(--text-muted)}
.tile .val{font-size:24px;font-weight:600;margin-top:4px;font-variant-numeric:tabular-nums}
.tile .sub{font-size:12px;color:var(--text-secondary);margin-top:2px}
.tile.good .val{color:var(--good)} .tile.warn .val{color:var(--warn)} .tile.crit .val{color:var(--crit)}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(500px,1fr));gap:16px;margin-top:16px}
.card{background:var(--surface-1);border:1px solid var(--border);border-radius:12px;padding:16px 16px 10px}
.card h3{margin:0 0 2px;font-size:14px}
.card .sub{margin:0 0 8px;font-size:12px;color:var(--text-secondary)}
.legend{display:flex;flex-wrap:wrap;gap:14px;margin:6px 0 0;font-size:12px;color:var(--text-secondary)}
.legend i{display:inline-block;width:10px;height:10px;border-radius:3px;margin-right:6px;vertical-align:-1px}
svg{width:100%;height:auto;display:block;overflow:visible}
.tick{fill:var(--text-muted);font-size:10px}
.axlab{fill:var(--text-secondary);font-size:11px}
.gl{stroke:var(--grid);stroke-width:1}
h2{font-size:16px;margin:30px 0 10px;padding-bottom:6px;border-bottom:1px solid var(--border)}
.warn-list{list-style:none;padding:0;margin:0}
.warn-list li{background:var(--surface-1);border:1px solid var(--border);border-left:4px solid var(--text-muted);
  border-radius:8px;padding:10px 14px;margin-bottom:8px}
.warn-list li.crit{border-left-color:var(--crit)}
.warn-list li.warn{border-left-color:var(--warn)}
.warn-list li.note{border-left-color:var(--s1)}
.warn-list b{font-size:11px;letter-spacing:.05em}
table{border-collapse:collapse;width:100%;font-size:12px;font-variant-numeric:tabular-nums}
dl.leg{margin:14px 0 4px;font-size:12px;line-height:1.55}
dl.leg dt{font-weight:600;color:var(--text-primary);margin-top:8px}
dl.leg dd{margin:0;color:var(--text-secondary)}
p.howto{font-size:12px;color:var(--text-secondary);border-left:3px solid var(--s1);padding-left:10px;margin:14px 0 2px}
th,td{padding:5px 8px;text-align:right;border-bottom:1px solid var(--border)}
th{color:var(--text-secondary);font-weight:600;text-align:right}
th:first-child,td:first-child{text-align:left}
details{background:var(--surface-1);border:1px solid var(--border);border-radius:12px;padding:12px 16px}
summary{cursor:pointer;font-size:13px;color:var(--text-secondary)}
.tt{position:fixed;pointer-events:none;background:var(--surface-1);border:1px solid var(--border);
    border-radius:8px;padding:8px 10px;font-size:12px;box-shadow:0 6px 18px #0002;opacity:0;transition:opacity .1s;z-index:9}
.tt b{display:block;margin-bottom:3px}
.tt span{display:flex;justify-content:space-between;gap:14px}
footer{margin-top:36px;font-size:12px;color:var(--text-muted)}
"""

    let private js = """
const $=(q,e=document)=>e.querySelector(q);
const TT=document.createElement('div');TT.className='tt';document.body.appendChild(TT);
function showTT(x,y,html){TT.innerHTML=html;TT.style.opacity=1;
  const r=TT.getBoundingClientRect();
  TT.style.left=Math.min(x+14,innerWidth-r.width-8)+'px';
  TT.style.top=Math.max(8,y-r.height-12)+'px';}
function hideTT(){TT.style.opacity=0}
const SV='http://www.w3.org/2000/svg';
function el(t,a){const e=document.createElementNS(SV,t);for(const k in a)e.setAttribute(k,a[k]);return e}
function nice(v,d=2){return (v===null||v===undefined||isNaN(v))?'-':Number(v).toFixed(d)}

function lineChart(host,cfg){
  const W=cfg.w||560,H=cfg.h||272,M={t:26,r:14,b:34,l:52};
  const s=el('svg',{viewBox:`0 0 ${W} ${H}`,role:'img'});
  const xs=cfg.x, xmin=Math.min(...xs), xmax=Math.max(...xs);
  let ymin=cfg.ymin, ymax=cfg.ymax;
  if(ymin===undefined||ymax===undefined){
    /// <summary>
    /// Calculates or returns a for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let a=[];cfg.series.forEach(se=>a=a.concat(se.y.filter(v=>v!=null&&!isNaN(v))));
    if(cfg.band){a=a.concat(cfg.band.lo,cfg.band.hi)}
    ymin=Math.min(...a);ymax=Math.max(...a);
    const p=(ymax-ymin)*0.08||1;ymin-=p;ymax+=p;
  }
  const X=v=>M.l+(v-xmin)/(xmax-xmin||1)*(W-M.l-M.r);
  const Y=v=>H-M.b-(v-ymin)/(ymax-ymin||1)*(H-M.t-M.b);
  for(let i=0;i<=4;i++){const v=ymin+(ymax-ymin)*i/4;
    s.appendChild(el('line',{x1:M.l,x2:W-M.r,y1:Y(v),y2:Y(v),class:'gl'}));
    const tx=el('text',{x:M.l-7,y:Y(v)+3,class:'tick','text-anchor':'end'});tx.textContent=nice(v,cfg.dec??0);s.appendChild(tx)}
  for(let i=0;i<=5;i++){const v=xmin+(xmax-xmin)*i/5;
    const tx=el('text',{x:X(v),y:H-M.b+16,class:'tick','text-anchor':'middle'});tx.textContent=nice(v,1);s.appendChild(tx)}
  const xl=el('text',{x:(M.l+W-M.r)/2,y:H-4,class:'axlab','text-anchor':'middle'});xl.textContent=cfg.xlab;s.appendChild(xl);
  const yl=el('text',{x:4,y:12,class:'axlab'});yl.textContent=cfg.ylab;s.appendChild(yl);
  if(cfg.band){
    /// <summary>
    /// Calculates or returns d for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let d='M'+xs.map((v,i)=>`${X(v)},${Y(cfg.band.hi[i])}`).join('L');
    d+='L'+xs.map((v,i)=>`${X(v)},${Y(cfg.band.lo[i])}`).reverse().join('L')+'Z';
    s.appendChild(el('path',{d:d,fill:'var(--band)',stroke:'none'}));
  }
  if(cfg.href!==undefined){s.appendChild(el('line',{x1:M.l,x2:W-M.r,y1:Y(cfg.href),y2:Y(cfg.href),
      stroke:'var(--crit)','stroke-width':1.5,'stroke-dasharray':'5 4'}))}
  cfg.series.forEach((se,k)=>{
    const d='M'+xs.map((v,i)=>`${X(v)},${Y(se.y[i])}`).join('L');
    s.appendChild(el('path',{d:d,fill:'none',stroke:se.color,'stroke-width':2,'stroke-linejoin':'round','stroke-linecap':'round'}));
  });
  const cur=el('line',{x1:0,x2:0,y1:M.t,y2:H-M.b,stroke:'var(--text-muted)','stroke-width':1,opacity:0});
  s.appendChild(cur);
  const dots=cfg.series.map(se=>{const c=el('circle',{r:4,fill:se.color,stroke:'var(--surface-1)','stroke-width':2,opacity:0});s.appendChild(c);return c});
  const hit=el('rect',{x:M.l,y:M.t,width:W-M.l-M.r,height:H-M.t-M.b,fill:'transparent'});
  s.appendChild(hit);
  hit.addEventListener('mousemove',ev=>{
    const b=s.getBoundingClientRect();
    const px=(ev.clientX-b.left)/b.width*W;
    /// <summary>
    /// Calculates or returns bi for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let bi=0,bd=1e9;xs.forEach((v,i)=>{const d=Math.abs(X(v)-px);if(d<bd){bd=d;bi=i}});
    cur.setAttribute('x1',X(xs[bi]));cur.setAttribute('x2',X(xs[bi]));cur.setAttribute('opacity',1);
    /// <summary>
    /// Calculates or returns h for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let h=`<b>${cfg.xlab.split('[')[0].trim()} = ${nice(xs[bi],2)}</b>`;
    cfg.series.forEach((se,k)=>{dots[k].setAttribute('cx',X(xs[bi]));dots[k].setAttribute('cy',Y(se.y[bi]));dots[k].setAttribute('opacity',1);
      h+=`<span><em style="font-style:normal;color:var(--text-secondary)">${se.name}</em><b style="display:inline;font-weight:600">${nice(se.y[bi],cfg.dec??1)}</b></span>`});
    showTT(ev.clientX,ev.clientY,h);
  });
  hit.addEventListener('mouseleave',()=>{cur.setAttribute('opacity',0);dots.forEach(d=>d.setAttribute('opacity',0));hideTT()});
  host.appendChild(s);
  if(cfg.series.length>1||cfg.legend){
    const lg=document.createElement('div');lg.className='legend';
    cfg.series.forEach(se=>{const d=document.createElement('span');d.innerHTML=`<i style="background:${se.color}"></i>${se.name}`;lg.appendChild(d)});
    if(cfg.bandName){const d=document.createElement('span');d.innerHTML=`<i style="background:var(--band)"></i>${cfg.bandName}`;lg.appendChild(d)}
    if(cfg.hrefName){const d=document.createElement('span');d.innerHTML=`<i style="background:var(--crit)"></i>${cfg.hrefName}`;lg.appendChild(d)}
    host.appendChild(lg);
  }
}

const SEQ=['var(--seq0)','var(--seq1)','var(--seq2)','var(--seq3)','var(--seq4)','var(--seq5)','var(--seq6)'];
function heat(host,cfg){
  const nx=cfg.x.length, ny=cfg.y.length;
  const W=560,H=Math.max(150,20+ny*15),M={t:8,r:14,b:34,l:52};
  const s=el('svg',{viewBox:`0 0 ${W} ${H}`,role:'img'});
  const cw=(W-M.l-M.r)/nx, ch=(H-M.t-M.b)/ny;
  let vmin=cfg.vmin,vmax=cfg.vmax;
  if(vmin===undefined){const f=cfg.v.flat();vmin=Math.min(...f);vmax=Math.max(...f)}
  const col=v=>{
    if(cfg.diverging){
      const m=cfg.mid;
      if(v<m){const t=Math.max(0,Math.min(1,(m-v)/(m-vmin||1)));return `color-mix(in oklab, var(--div-neg) ${Math.round(t*100)}%, var(--div-mid))`}
      const t=Math.max(0,Math.min(1,(v-m)/(vmax-m||1)));return `color-mix(in oklab, var(--div-pos) ${Math.round(t*100)}%, var(--div-mid))`;
    }
    const t=Math.max(0,Math.min(0.999,(v-vmin)/(vmax-vmin||1)));return SEQ[Math.floor(t*SEQ.length)];
  };
  for(let j=0;j<ny;j++)for(let i=0;i<nx;i++){
    const v=cfg.v[j][i];
    const r=el('rect',{x:M.l+i*cw,y:M.t+(ny-1-j)*ch,width:Math.max(0.6,cw-0.6),height:Math.max(0.6,ch-0.6),fill:col(v)});
    r.addEventListener('mousemove',ev=>showTT(ev.clientX,ev.clientY,
      `<b>z = ${nice(cfg.x[i],2)} m · y = ${nice(cfg.y[j],3)} m</b><span><em style="font-style:normal;color:var(--text-secondary)">${cfg.name}</em><b style="display:inline">${nice(v,cfg.dec??2)}</b></span>`));
    r.addEventListener('mouseleave',hideTT);
    s.appendChild(r);
  }
  for(let i=0;i<=5;i++){const v=cfg.x[0]+(cfg.x[nx-1]-cfg.x[0])*i/5;
    const t=el('text',{x:M.l+(W-M.l-M.r)*i/5,y:H-M.b+16,class:'tick','text-anchor':'middle'});t.textContent=nice(v,1);s.appendChild(t)}
  [0,ny-1].forEach(j=>{const t=el('text',{x:M.l-7,y:M.t+(ny-1-j)*ch+ch/2+3,class:'tick','text-anchor':'end'});
    t.textContent=nice(cfg.y[j],2);s.appendChild(t)});
  const xl=el('text',{x:(M.l+W-M.r)/2,y:H-4,class:'axlab','text-anchor':'middle'});xl.textContent='z lungo l’apparecchio [m]';s.appendChild(xl);
  const yl=el('text',{x:4,y:12,class:'axlab'});yl.textContent='y [m] (altezza nel fascio)';s.appendChild(yl);
  host.appendChild(s);
  const lg=document.createElement('div');lg.className='legend';
  const steps=cfg.diverging?[vmin,cfg.mid,vmax]:[vmin,(vmin+vmax)/2,vmax];
  steps.forEach(v=>{const d=document.createElement('span');d.innerHTML=`<i style="background:${col(v)}"></i>${nice(v,cfg.dec??2)}`;lg.appendChild(d)});
  const d=document.createElement('span');d.innerHTML=cfg.name;lg.appendChild(d);
  host.appendChild(lg);
}
"""

    /// <summary>
    /// Calculates or returns build for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let build (r: DesignResult) =
        let c = r.Case
        let ax = r.Axial |> List.toArray
        let ny = List.length r.Bands
        let nz = ax.Length
        let cellAt =
            let d = System.Collections.Generic.Dictionary<int * int, CellResult>()
            r.Cells |> List.iter (fun x -> d.[(x.I, x.J)] <- x)
            fun i j -> d.[(i, j)]
        let zs = ax |> Array.map (fun a -> a.Z)
        let ys = r.Bands |> List.map (fun b -> b.Y)
        let mat name dec diverging mid vmin vmax (f: CellResult -> float) =
            let rows =
                [ for j in 0 .. ny - 1 ->
                    "[" + String.Join(",", [ for i in 0 .. nz - 1 -> n (f (cellAt i j)) ]) + "]" ]
            sprintf "{name:\"%s\",dec:%d,diverging:%b,mid:%s,vmin:%s,vmax:%s,x:%s,y:%s,v:[%s]}"
                name dec diverging (n mid) (n vmin) (n vmax) (arr zs) (arr ys)
                (String.Join(",", rows))

        let stressAt =
            let d = System.Collections.Generic.Dictionary<int * int, StressCell>()
            r.Stress.Cells
            |> List.filter (fun x -> x.Component = "TUBI" && x.C = 0)
            |> List.iter (fun x -> d.[(x.I, x.J)] <- x)
            fun i j ->
                match d.TryGetValue((i, j)) with
                | true, v -> Some v
                | _ -> None
        let matS name dec diverging mid vmin vmax (f: StressCell -> float) =
            let rows =
                [ for j in 0 .. ny - 1 ->
                    "[" + String.Join(",", [ for i in 0 .. nz - 1 -> n (match stressAt i j with Some s -> f s | None -> 0.0) ]) + "]" ]
            sprintf "{name:\"%s\",dec:%d,diverging:%b,mid:%s,vmin:%s,vmax:%s,x:%s,y:%s,v:[%s]}"
                name dec diverging (n mid) (n vmin) (n vmax) (arr zs) (arr ys)
                (String.Join(",", rows))

        let hot = r.Cells |> List.filter (fun x -> not x.InFerrule)
        let dnbMin = hot |> List.map (fun x -> x.DNBR) |> List.min
        let qMax = hot |> List.map (fun x -> x.QFluxOut) |> List.max
        let tmMax = r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max
        let alphaMax = r.Cells |> List.map (fun x -> x.Alpha) |> List.max
        let cls (ok: bool) (warn: bool) = if ok then "good" elif warn then "warn" else "crit"

        let tile lab v sub k =
            sprintf "<div class=\"tile %s\"><div class=\"lab\">%s</div><div class=\"val\">%s</div><div class=\"sub\">%s</div></div>"
                k (esc lab) (esc v) (esc sub)

        let tiles =
            String.concat "" [
                tile "Potenza scambiata" (sprintf "%.1f MW" (r.Duty / 1e6)) "gas -> vapore" ""
                tile "Vapore prodotto" (sprintf "%.0f t/h" (r.SteamProduction * 3.6)) (sprintf "%.2f kg/s" r.SteamProduction) ""
                tile "T gas uscita" (sprintf "%.1f °C" (kToC r.TGasOutMean)) (sprintf "spread bande %.0f K" (r.TGasOutMax - r.TGasOutMin)) ""
                tile "U medio" (sprintf "%.0f W/m²K" r.UMean) (sprintf "LMTD %.0f K" r.LmtdMean) ""
                tile "Rapporto circolazione" (sprintf "%.1f" r.Circulation.CirculationRatio)
                     (sprintf "minimo di progetto 10 - %.0f t/h circolanti" (r.Circulation.CircFlow * 3.6))
                     (cls (r.Circulation.CirculationRatio >= 12.0) (r.Circulation.CirculationRatio >= 10.0))
                tile "Flusso termico max" (sprintf "%.0f kW/m²" (qMax / 1000.0)) "picco locale" (cls (qMax < 250e3) (qMax < 350e3))
                tile "DNBR locale minimo" (sprintf "%.2f" dnbMin) "CHF fascio / q locale" (cls (dnbMin >= 2.0) (dnbMin >= 1.0))
                tile "T metallo max" (sprintf "%.0f °C" (kToC tmMax)) (sprintf "limite %s: %.0f °C" c.Material.Name c.Material.TmaxDesign)
                     (cls (kToC tmMax < 0.92 * c.Material.TmaxDesign) (kToC tmMax < c.Material.TmaxDesign))
                tile "Alpha max nel fascio" (sprintf "%.2f" alphaMax) "banda superiore" (cls (alphaMax < 0.7) (alphaMax < 0.8))
                tile "Dilatazione differenziale"
                     (sprintf "%.1f mm" ((r.Expansions |> List.find (fun e -> e.Label.StartsWith "DIFFERENZIALE tubo")).DeltaL * 1000.0))
                     "tubo piu' caldo - mantello" ""
                tile "dP lato gas" (sprintf "%.0f mbar" (r.DpGas / 100.0)) (sprintf "limite 0.3 bar") (cls (r.DpGas < 0.3e5) (r.DpGas < 0.4e5))
            ]

        let sevCls = function Critical -> "crit" | Warning -> "warn" | Note -> "note"
        let sevLab = function Critical -> "CRITICO" | Warning -> "ATTENZIONE" | Note -> "NOTA"
        let sevRank = function Critical -> 0 | Warning -> 1 | Note -> 2
        let findingsHtml =
            if r.Findings.IsEmpty then "<li class=\"note\"><b>OK</b><br>Nessuna criticita' rilevata.</li>"
            else
                r.Findings
                |> List.sortBy (fun f -> (sevRank f.Severity, f.Area))
                |> List.map (fun f ->
                    sprintf "<li class=\"%s\"><b>%s &middot; %s</b><br><span style=\"font-size:14px;font-weight:600\">%s</span><br><table style=\"margin-top:6px\"><tbody><tr><td style=\"text-align:left;width:90px;color:var(--text-muted)\">valore</td><td style=\"text-align:left\">%s</td></tr><tr><td style=\"text-align:left;color:var(--text-muted)\">criterio</td><td style=\"text-align:left\">%s</td></tr><tr><td style=\"text-align:left;color:var(--text-muted)\">dove</td><td style=\"text-align:left\"><b>%s</b></td></tr>%s%s</tbody></table></li>"
                        (sevCls f.Severity) (sevLab f.Severity) (esc f.Area) (esc f.Title)
                        (esc f.Value) (esc f.Limit) (esc f.Where)
                        (if f.Detail = "" then "" else sprintf "<tr><td style=\"text-align:left;color:var(--text-muted)\">perche'</td><td style=\"text-align:left\">%s</td></tr>" (esc f.Detail))
                        (if f.Action = "" then "" else sprintf "<tr><td style=\"text-align:left;color:var(--text-muted)\">azione</td><td style=\"text-align:left\">%s</td></tr>" (esc f.Action)))
                |> String.concat ""

        let warnHtml =
            if r.Warnings.IsEmpty then "<li class=\"note\"><b>OK</b><br>Nessuna anomalia rilevata dai criteri implementati.</li>"
            else
                r.Warnings
                |> List.map (fun x ->
                    let k = if x.StartsWith "CRITICO" then "crit" elif x.StartsWith "ATTENZIONE" then "warn" else "note"
                    let head = x.Split([| " - " |], 2, StringSplitOptions.None)
                    sprintf "<li class=\"%s\"><b>%s</b><br>%s</li>" k (esc head.[0])
                        (esc (if head.Length > 1 then head.[1] else "")))
                |> String.concat ""

        let bandRows =
            [ for j in 0 .. ny - 1 ->
                let cj = [ for i in 0 .. nz - 1 -> cellAt i j ]
                let last = cj |> List.last
                let hotj = cj |> List.filter (fun x -> not x.InFerrule)
                sprintf "<tr><td>%d</td><td>%.3f</td><td>%.0f</td><td>%.1f</td><td>%.0f</td><td>%.0f</td><td>%.4f</td><td>%.3f</td><td>%.2f</td></tr>"
                    j r.Bands.[j].Y r.Bands.[j].NTubes (kToC last.TGas)
                    ((hotj |> List.map (fun x -> x.QFluxOut) |> List.max) / 1000.0)
                    (kToC (cj |> List.map (fun x -> x.TMetalIn) |> List.max))
                    (cj |> List.map (fun x -> x.XOut) |> List.max)
                    (cj |> List.map (fun x -> x.Alpha) |> List.max)
                    (hotj |> List.map (fun x -> x.DNBR) |> List.min) ]
            |> String.concat ""

        let axialRows =
            let step = max 1 (nz / 40)
            [ for i in 0 .. nz - 1 do
                if i % step = 0 || i = nz - 1 then
                    let a = ax.[i]
                    yield sprintf "<tr><td>%.2f</td><td>%.0f</td><td>%.0f</td><td>%.0f</td><td>%.0f</td><td>%.0f</td><td>%.4f</td><td>%.3f</td><td>%.2f</td><td>%.1f</td><td>%.2f</td></tr>"
                              a.Z (kToC a.TGasMean) (a.TGasMax - a.TGasMin) (a.QFluxMean / 1000.0) (a.QFluxMax / 1000.0)
                              (kToC a.TMetalInMax) a.XTop a.AlphaTop a.DNBRMin a.WFieldLin a.VelMixOut ]
            |> String.concat ""

        let data =
            let sb = StringBuilder()
            sb.Append("const D={") |> ignore
            sb.Append("z:").Append(arr zs).Append(",") |> ignore
            sb.Append("tgas:").Append(arr (ax |> Array.map (fun a -> kToC a.TGasMean))).Append(",") |> ignore
            sb.Append("tgasmin:").Append(arr (ax |> Array.map (fun a -> kToC a.TGasMin))).Append(",") |> ignore
            sb.Append("tgasmax:").Append(arr (ax |> Array.map (fun a -> kToC a.TGasMax))).Append(",") |> ignore
            sb.Append("qmed:").Append(arr (ax |> Array.map (fun a -> a.QFluxMean / 1000.0))).Append(",") |> ignore
            sb.Append("qmax:").Append(arr (ax |> Array.map (fun a -> a.QFluxMax / 1000.0))).Append(",") |> ignore
            sb.Append("tmi:").Append(arr (ax |> Array.map (fun a -> kToC a.TMetalInMax))).Append(",") |> ignore
            sb.Append("tmo:").Append(arr (ax |> Array.map (fun a -> kToC a.TMetalOutMax))).Append(",") |> ignore
            sb.Append("tmm:").Append(arr [ for i in 0 .. nz - 1 -> kToC ([ for j in 0 .. ny - 1 -> (cellAt i j).TMetalMid ] |> List.max) ]).Append(",") |> ignore
            sb.Append("tsat:").Append(arr (Array.create nz (kToC r.Sat.Tsat))).Append(",") |> ignore
            sb.Append("steam:").Append(arr (ax |> Array.map (fun a -> a.SteamLin))).Append(",") |> ignore
            sb.Append("steamcum:").Append(arr (ax |> Array.map (fun a -> a.SteamCum * 3.6))).Append(",") |> ignore
            sb.Append("xtop:").Append(arr (ax |> Array.map (fun a -> a.XTop))).Append(",") |> ignore
            sb.Append("alpha:").Append(arr (ax |> Array.map (fun a -> a.AlphaTop))).Append(",") |> ignore
            sb.Append("dnbr:").Append(arr (ax |> Array.map (fun a -> a.DNBRMin))).Append(",") |> ignore
            sb.Append("vliq:").Append(arr (ax |> Array.map (fun a -> a.VelLiqIn))).Append(",") |> ignore
            sb.Append("vmix:").Append(arr (ax |> Array.map (fun a -> a.VelMixOut))).Append(",") |> ignore
            sb.Append("vvap:").Append(arr (ax |> Array.map (fun a -> a.VelVapOut))).Append(",") |> ignore
            sb.Append("vaxb:").Append(arr (ax |> Array.map (fun a -> a.VelAxialBottom))).Append(",") |> ignore
            sb.Append("vaxt:").Append(arr (ax |> Array.map (fun a -> a.VelAxialTop))).Append(",") |> ignore
            sb.Append("wfield:").Append(arr (ax |> Array.map (fun a -> a.WFieldLin))).Append(",") |> ignore
            sb.Append("wbyp:").Append(arr (ax |> Array.map (fun a -> a.WBypassLin))).Append(",") |> ignore
            sb.Append("vgas:").Append(arr [ for i in 0 .. nz - 1 -> [ for j in 0 .. ny - 1 -> (cellAt i j).VelGas ] |> List.average ]).Append(",") |> ignore
            sb.Append("hmTmi:").Append(mat "T metallo interna [°C]" 1 false 0.0
                                          (kToC (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.min))
                                          (kToC (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max))
                                          (fun x -> kToC x.TMetalIn)).Append(",") |> ignore
            sb.Append("hmAl:").Append(mat "frazione di vuoto" 3 false 0.0 0.0
                                         (r.Cells |> List.map (fun x -> x.Alpha) |> List.max)
                                         (fun x -> x.Alpha)).Append(",") |> ignore
            sb.Append("hmQ:").Append(mat "flusso termico [kW/m²]" 0 false 0.0 0.0
                                        ((r.Cells |> List.map (fun x -> x.QFluxOut) |> List.max) / 1000.0)
                                        (fun x -> x.QFluxOut / 1000.0)).Append(",") |> ignore
            sb.Append("hmD:").Append(mat "DNBR locale" 2 true 2.0 0.0
                                        (min 6.0 (r.Cells |> List.map (fun x -> x.DNBR) |> List.max))
                                        (fun x -> min 6.0 x.DNBR)).Append(",") |> ignore
            let sTub = r.Stress.Cells |> List.filter (fun x -> x.Component = "TUBI")
            sb.Append("hmVM:").Append(
                matS "sigma von Mises [MPa]" 0 false 0.0
                    ((sTub |> List.map (fun x -> x.SigmaVMMax) |> List.min) / 1e6)
                    ((sTub |> List.map (fun x -> x.SigmaVMMax) |> List.max) / 1e6)
                    (fun x -> x.SigmaVMMax / 1e6)).Append(",") |> ignore
            sb.Append("hmUse:").Append(
                matS "utilizzo su Sy [%]" 0 false 0.0 0.0
                    (100.0 * (sTub |> List.map (fun x -> x.Utilisation) |> List.max))
                    (fun x -> 100.0 * x.Utilisation)).Append(",") |> ignore
            sb.Append("hmSth:").Append(
                matS "sigma circonferenziale [MPa]" 0 false
                    0.0
                    ((sTub |> List.collect (fun x -> x.Points |> List.map (fun p -> p.SigmaTheta)) |> List.min) / 1e6)
                    ((sTub |> List.collect (fun x -> x.Points |> List.map (fun p -> p.SigmaTheta)) |> List.max) / 1e6)
                    (fun x -> (x.Points |> List.map (fun p -> p.SigmaTheta) |> List.min) / 1e6)).Append(",") |> ignore
            let vsw =
                match r.Valve with
                | Some v -> v.Sweep |> List.toArray
                | None -> [||]
            sb.Append("vang:").Append(arr (vsw |> Array.map (fun p -> p.OpenDeg))).Append(",") |> ignore
            sb.Append("vtmix:").Append(arr (vsw |> Array.map (fun p -> kToC p.TMixed))).Append(",") |> ignore
            sb.Append("vttub:").Append(arr (vsw |> Array.map (fun p -> kToC p.TOutTubes))).Append(",") |> ignore
            sb.Append("vtbyp:").Append(arr (vsw |> Array.map (fun p -> kToC p.TOutBypass))).Append(",") |> ignore
            sb.Append("vtlin:").Append(arr (vsw |> Array.map (fun p -> kToC p.TLinerMax))).Append(",") |> ignore
            sb.Append("vfrac:").Append(arr (vsw |> Array.map (fun p -> 100.0 * p.Fraction))).Append(",") |> ignore
            sb.Append("vsteam:").Append(arr (vsw |> Array.map (fun p -> p.Steam * 3.6))).Append(",") |> ignore
            sb.Append("vduty:").Append(arr (vsw |> Array.map (fun p -> p.Duty / 1e6))).Append(",") |> ignore
            sb.Append("vdpv:").Append(arr (vsw |> Array.map (fun p -> p.DpValve / 100.0))).Append(",") |> ignore
            sb.Append("vlogz:").Append(arr (vsw |> Array.map (fun p -> log10 p.Zeta))) |> ignore
            sb.Append("};") |> ignore
            sb.ToString()

        let body = StringBuilder()
        let card id title sub =
            body.Append(sprintf "<div class=\"card\"><h3>%s</h3><p class=\"sub\">%s</p><div id=\"%s\"></div></div>"
                            (esc title) (esc sub) id) |> ignore

        body.Append("<div class=\"grid\">") |> ignore
        card "c1" "Temperatura del gas lungo il tubo" "media pesata sui tubi; la banda mostra la dispersione fra la banda più bassa e la più alta del fascio"
        card "c2" "Flusso termico lato acqua" "medio sulle bande e massimo locale, riferiti alla superficie esterna"
        card "c3" "Temperature metalliche (massimo sulle bande)" "interna, mezzeria spessore, esterna; Tsat come riferimento"
        card "c4" "Generazione di vapore" "per metro di lunghezza dell'apparecchio"
        card "c5" "Titolo e frazione di vuoto in uscita dal fascio" "valori all'uscita della banda superiore"
        card "c6" "Velocità lato mantello" "attraversamento fascio e canali assiali"
        card "c7" "Portata circolante nel fascio" "per metro di lunghezza; segue la produzione locale di vapore (CR locale uniforme)"
        card "c9" "Vapore cumulato lungo l'apparecchio" "portata di vapore generata da 0 a z"
        card "c8" "DNBR locale minimo" "rapporto fra CHF di fascio corretto per il titolo e flusso termico locale"
        body.Append("</div>") |> ignore

        body.Append("<h2>Mappe 2-D (asse apparecchio × altezza nel fascio)</h2><div class=\"grid\">") |> ignore
        card "h1" "Temperatura metallica interna" "ogni cella è una banda di tubi in una sezione assiale"
        card "h2" "Frazione di vuoto lato mantello" "cresce salendo nel fascio: la banda superiore è la critica"
        card "h3" "Flusso termico" "riferito alla superficie esterna del tubo"
        card "h4" "DNBR locale" "blu = margine, rosso = margine insufficiente; il neutro è DNBR = 2"
        card "h5" "Tensione equivalente di von Mises" "combinazione di Lamé (pressione esterna prevalente), gradiente termico radiale e carico assiale"
        card "h6" "Utilizzo dello snervamento" "sigma di von Mises / Sy alla temperatura locale; il neutro è il 66 %"
        card "h7" "Tensione circonferenziale" "negativa = COMPRESSIONE: l'acqua a 118 bar preme dall'esterno, il gas dentro sta a 35 bar"
        body.Append("</div>") |> ignore

        match r.Valve with
        | None -> ()
        | Some v ->
            body.Append(sprintf "<h2>Valvola a farfalla del by-pass &mdash; ripartizione dei flussi e temperature</h2><p class=\"sub\">Posizione normale <b>%.1f&deg; di apertura</b> (chiusura %.1f&deg;, zeta = %.0f); finestra ammessa <b>%.1f&deg; &ndash; %.1f&deg;</b>. Il fascio e il by-pass sono due rami in parallelo: la portata si ripartisce in modo che dissipino la stessa caduta di pressione, quindi &egrave; la valvola a decidere tutto.</p><div class=\"grid\">"
                            v.Normal.OpenDeg v.Normal.ClosureDeg v.Normal.Zeta v.MinOpen.OpenDeg v.MaxOpen.OpenDeg) |> ignore
            card "v1" "Temperatura MISCELATA in funzione dell'angolo" "è la grandezza regolata; in verde l'uscita dai soli tubi, che cambia pochissimo"
            card "v1b" "Temperatura nel by-pass e nel liner" "il gas deviato non si raffredda: aprendo, sia lui sia il liner si avvicinano alla temperatura d'ingresso"
            card "v2" "Frazione di portata deviata nel by-pass" "non è un dato di progetto: è il risultato dell'equilibrio fra i due rami"
            card "v3" "Coefficiente di perdita della valvola" "scala logaritmica: zeta varia di oltre tre ordini di grandezza sulla corsa"
            card "v4" "Vapore prodotto e potenza scambiata" "aprendo il by-pass si recupera meno calore"
            body.Append("</div>") |> ignore

        let script = """
lineChart($('#c1'),{x:D.z,xlab:'z lungo l’apparecchio [m]',ylab:'T gas [°C]',dec:0,
  band:{lo:D.tgasmin,hi:D.tgasmax},bandName:'dispersione fra bande',
  series:[{name:'T gas media',y:D.tgas,color:'var(--s1)'}],legend:true});
lineChart($('#c2'),{x:D.z,xlab:'z [m]',ylab:'q″ [kW/m²]',dec:0,
  series:[{name:'q″ medio',y:D.qmed,color:'var(--s1)'},{name:'q″ massimo locale',y:D.qmax,color:'var(--s2)'}]});
lineChart($('#c3'),{x:D.z,xlab:'z [m]',ylab:'T [°C]',dec:0,
  series:[{name:'metallo interna',y:D.tmi,color:'var(--s1)'},{name:'mezzeria spessore',y:D.tmm,color:'var(--s2)'},
          {name:'metallo esterna',y:D.tmo,color:'var(--s3)'},{name:'Tsat',y:D.tsat,color:'var(--s4)'}]});
lineChart($('#c4'),{x:D.z,xlab:'z [m]',ylab:'generazione vapore [kg/(s·m)]',dec:2,
  series:[{name:'generazione locale',y:D.steam,color:'var(--s1)'}],legend:true});
lineChart($('#c9'),{x:D.z,xlab:'z [m]',ylab:'vapore cumulato [t/h]',dec:0,
  series:[{name:'vapore cumulato',y:D.steamcum,color:'var(--s2)'}],legend:true});
lineChart($('#c5'),{x:D.z,xlab:'z [m]',ylab:'x , alpha [-]',dec:3,
  series:[{name:'titolo x',y:D.xtop,color:'var(--s1)'},{name:'frazione di vuoto',y:D.alpha,color:'var(--s2)'}]});
lineChart($('#c6'),{x:D.z,xlab:'z [m]',ylab:'v [m/s]',dec:2,
  series:[{name:'liquido ingresso fascio',y:D.vliq,color:'var(--s1)'},{name:'miscela uscita fascio',y:D.vmix,color:'var(--s2)'},
          {name:'vapore uscita fascio',y:D.vvap,color:'var(--s3)'},{name:'assiale plenum superiore',y:D.vaxt,color:'var(--s4)'}]});
lineChart($('#c7'),{x:D.z,xlab:'z [m]',ylab:'w [kg/(s·m)]',dec:1,
  series:(Math.max(...D.wbyp.map(Math.abs))>1e-6
    ?[{name:'campo tubi',y:D.wfield,color:'var(--s1)'},{name:'canali liberi',y:D.wbyp,color:'var(--s2)'}]
    :[{name:'attraverso il fascio',y:D.wfield,color:'var(--s1)'}]),legend:true});
lineChart($('#c8'),{x:D.z,xlab:'z [m]',ylab:'DNBR [-]',dec:2,href:2,hrefName:'soglia di progetto = 2',
  series:[{name:'DNBR minimo nella sezione',y:D.dnbr,color:'var(--s1)'}],legend:true});
heat($('#h1'),D.hmTmi);heat($('#h2'),D.hmAl);heat($('#h3'),D.hmQ);heat($('#h4'),D.hmD);
heat($('#h5'),D.hmVM);heat($('#h6'),D.hmUse);heat($('#h7'),D.hmSth);
if(D.vang.length){
lineChart($('#v1'),{x:D.vang,xlab:'apertura della farfalla [°]',ylab:'T [°C]',dec:1,legend:true,
  series:[{name:'T MISCELATA (regolata)',y:D.vtmix,color:'var(--s1)'},{name:'uscita dai soli tubi',y:D.vttub,color:'var(--s3)'}]});
lineChart($('#v1b'),{x:D.vang,xlab:'apertura della farfalla [°]',ylab:'T [°C]',dec:0,legend:true,
  series:[{name:'uscita dal by-pass',y:D.vtbyp,color:'var(--s2)'},{name:'liner, massimo',y:D.vtlin,color:'var(--s4)'}]});
lineChart($('#v2'),{x:D.vang,xlab:'apertura della farfalla [°]',ylab:'portata deviata [%]',dec:2,legend:true,
  series:[{name:'frazione nel by-pass',y:D.vfrac,color:'var(--s2)'}]});
lineChart($('#v3'),{x:D.vang,xlab:'apertura della farfalla [°]',ylab:'log10(zeta)',dec:2,legend:true,
  series:[{name:'log10 del coefficiente di perdita',y:D.vlogz,color:'var(--s1)'}]});
lineChart($('#v4'),{x:D.vang,xlab:'apertura della farfalla [°]',ylab:'vapore [t/h] , potenza [MW]',dec:1,legend:true,
  series:[{name:'vapore prodotto [t/h]',y:D.vsteam,color:'var(--s1)'},{name:'potenza scambiata [MW]',y:D.vduty,color:'var(--s2)'}]});
}
"""

        let html = StringBuilder()
        html.Append("<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\">") |> ignore
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">") |> ignore
        html.Append("<title>WHB - report di calcolo</title><style>").Append(css).Append("</style></head>") |> ignore
        html.Append("<body class=\"viz-root\"><button class=\"toggle\" onclick=\"document.documentElement.dataset.theme=document.documentElement.dataset.theme==='dark'?'light':'dark'\">tema</button><div class=\"wrap\">") |> ignore
        html.Append("<header><h1>WHB / PGC a tubi da fumo - report di calcolo</h1><p>")
            .Append(esc c.Name).Append("</p><p>")
            .Append(sprintf "%d tubi OD %.1f x %.2f mm, L %.3f m, passo %.1f mm triangolare 60° - mantello ID %.0f mm, OTL %.1f mm, ITL %.0f mm"
                        c.Tube.NTubes (c.Tube.Do * 1000.0) ((c.Tube.Do - c.Tube.Di) * 500.0) c.Tube.Length
                        (c.Tube.Pitch * 1000.0) (c.Tube.ShellId * 1000.0) (c.Tube.Otl * 1000.0) (c.Tube.Itl * 1000.0))
            .Append("</p><p>")
            .Append(sprintf "Discretizzazione %d sezioni assiali x %d bande = %d celle - generato %s"
                        nz ny (nz * ny) (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)))
            .Append("</p></header>") |> ignore
        html.Append("<div class=\"tiles\">").Append(tiles).Append("</div>") |> ignore
        let qmaxC = r.Cells |> List.filter (fun x -> not x.InFerrule) |> List.maxBy (fun x -> x.QFluxOut)
        let tG = kToC qmaxC.TGas
        let tMi = kToC qmaxC.TMetalIn
        let tMo = kToC qmaxC.TMetalOut
        let tWb = kToC qmaxC.TWallBoil
        let tS = kToC r.Sat.Tsat
        let dTot = tG - tS
        let bar (lab: string) (v: float) =
            sprintf "<tr><td>%s</td><td>%.0f K</td><td>%.0f %%</td><td style=\"width:55%%\"><div style=\"height:9px;border-radius:4px;background:var(--s1);width:%.1f%%\"></div></td></tr>"
                (esc lab) v (100.0 * v / dTot) (100.0 * v / dTot)
        html.Append("<h2>Dove va a finire il salto di temperatura</h2>")
            .Append("<div class=\"card\"><p class=\"sub\">")
            .Append(sprintf "Nel punto di flusso massimo (z = %.2f m, y = %+.2f m) il calore va dal gas a %.0f &deg;C all'acqua che bolle a %.1f &deg;C, attraversando quattro resistenze in serie. Ogni resistenza si paga con un salto di temperatura; la somma fa %.0f &deg;C."
                        qmaxC.Z qmaxC.Y tG tS dTot)
            .Append("</p><table><tbody>")
            .Append(bar "Film di gas + sporco lato gas" (tG - tMi))
            .Append(bar "Spessore del metallo" (tMi - tMo))
            .Append(bar "Deposito lato acqua" (tMo - tWb))
            .Append(bar "Film di ebollizione" (tWb - tS))
            .Append("</tbody></table>")
            .Append("<p class=\"howto\">Il film di gas e' il collo di bottiglia: e' per questo che il metallo resta vicino alla temperatura dell'acqua e non a quella del gas, e un tubo di acciaio puo' stare in un gas a quasi 1000 &deg;C. Il secondo contributo per importanza non e' l'ebollizione ma <b>il deposito di ossidi lato acqua</b>: il metallo e' caldo per lo sporco, non per il regime di ebollizione.</p>")
            .Append("</div>") |> ignore
        html.Append("<h2>Profili lungo l'asse dell'apparecchio</h2>") |> ignore
        html.Append(body.ToString()) |> ignore
        let nC = r.Findings |> List.filter (fun f -> f.Severity = Critical) |> List.length
        let nW = r.Findings |> List.filter (fun f -> f.Severity = Warning) |> List.length
        html.Append(sprintf "<h2>Criticita' rilevate e dove si collocano &mdash; %d critiche, %d attenzioni</h2>" nC nW)
            .Append("<ul class=\"warn-list\">").Append(findingsHtml).Append("</ul>") |> ignore
        html.Append("<h2>Tabelle</h2>") |> ignore
        html.Append("<details open><summary>Effetto fascio: sintesi per banda orizzontale</summary><table><thead><tr>")
            .Append("<th>banda</th><th>y [m]</th><th>n. tubi</th><th>T gas out [°C]</th><th>q&#8243; max [kW/m²]</th>")
            .Append("<th>T met. int. max [°C]</th><th>x uscita</th><th>alpha</th><th>DNBR min</th></tr></thead><tbody>")
            .Append(bandRows).Append("</tbody></table>")
            .Append("<dl class=\"leg\">")
            .Append("<dt>banda</dt><dd>Fascia orizzontale del fascio. La <b>0 e' la piu' bassa</b>, l'ultima la piu' alta. L'acqua attraversa il fascio dal basso verso l'alto, quindi le bande sono percorse in serie.</dd>")
            .Append("<dt>y [m]</dt><dd>Quota del centro banda dall'asse del mantello: negativa sotto, positiva sopra.</dd>")
            .Append("<dt>n. tubi</dt><dd>Tubi contenuti nella banda: area intubata della fascia divisa per l'area di competenza di un tubo (passo&sup2;&middot;sin60&deg;). La somma da' il numero totale.</dd>")
            .Append("<dt>T gas out [&deg;C]</dt><dd>Temperatura del gas all'uscita dei tubi di quella banda. I tubi sono canali in parallelo: ogni banda ha il suo profilo.</dd>")
            .Append("<dt>q&#8243; max [kW/m&sup2;]</dt><dd>Massimo flusso termico lungo la banda, riferito alla superficie <b>esterna</b>. Il tratto sotto ferrula e' escluso.</dd>")
            .Append("<dt>T met. int. max [&deg;C]</dt><dd>Massima temperatura del metallo sulla superficie <b>interna</b> (lato gas): e' il punto piu' caldo della parete.</dd>")
            .Append("<dt>x uscita</dt><dd><b>Titolo massico</b>: portata di vapore / portata totale. Cresce salendo perche' ogni banda aggiunge il vapore che ha prodotto.</dd>")
            .Append("<dt>alpha</dt><dd><b>Frazione di vuoto</b>: area occupata dal vapore / area totale. Molto piu' grande del titolo perche' il vapore e' meno denso: a 118 bar x = 0.10 da' gia' alpha = 0.45. E' alpha, non x, a dire se i ranghi alti restano bagnati (limite pratico 0.7).</dd>")
            .Append("<dt>DNBR min</dt><dd>Minimo rapporto fra flusso termico critico (CHF) e flusso effettivo. E' un <b>margine</b>: 3 = si lavora a un terzo del limite, 1 = al limite, &lt;1 = criterio violato.</dd>")
            .Append("</dl>")
            .Append("<p class=\"howto\">Come si legge: la banda 0 riceve acqua satura e lavora nelle condizioni migliori; salendo, titolo e frazione di vuoto crescono e il margine sul DNB cala. La temperatura del gas in uscita cambia invece pochissimo fra le bande, perche' la resistenza lato acqua e' una frazione minima di quella totale. <b>L'effetto fascio non si vede sul gas: si vede sul DNB della banda superiore.</b></p>")
            .Append("</details>") |> ignore
        html.Append("<details><summary>Profilo assiale (estratto)</summary><table><thead><tr>")
            .Append("<th>z [m]</th><th>T gas [°C]</th><th>spread [K]</th><th>q&#8243; med</th><th>q&#8243; max</th>")
            .Append("<th>T met. int. [°C]</th><th>x top</th><th>alpha</th><th>DNBR</th><th>w campo</th><th>v miscela</th></tr></thead><tbody>")
            .Append(axialRows).Append("</tbody></table>")
            .Append("<dl class=\"leg\">")
            .Append("<dt>z [m]</dt><dd>Ascissa dall'imbocco gas. Maglia graduata: celle di ~20 mm all'imbocco, piu' larghe verso l'uscita.</dd>")
            .Append("<dt>spread [K]</dt><dd>Differenza di temperatura del gas fra la banda piu' calda e la piu' fredda alla stessa ascissa: misura diretta dell'effetto fascio.</dd>")
            .Append("<dt>x top / alpha</dt><dd>Titolo e frazione di vuoto della miscela che esce dalla banda superiore, cioe' quella che va ai riser.</dd>")
            .Append("<dt>w campo [kg/(s&middot;m)]</dt><dd>Portata d'acqua che attraversa il fascio per metro di apparecchio: segue la produzione locale di vapore, quindi decade lungo z come il flusso termico.</dd>")
            .Append("<dt>v miscela [m/s]</dt><dd>Velocita' della miscela in uscita dal fascio, con densita' omogenea.</dd>")
            .Append("</dl></details>") |> ignore
        html.Append(sprintf "<footer>Correlazioni: gas %s + correzione proprieta' variabili, effetto d'imbocco e irraggiamento CKTI/Blokh; ebollizione %s con fattore di fascio di Palen Fb = %.1f e termine convettivo bifase; proprieta' acqua/vapore IAPWS-IF97. Shift: %s.</footer>"
                        (esc (GasSide.correlationName c.Gas.Correlation))
                        (esc (WaterSide.poolBoilingName c.Water.Correlation))
                        c.Water.BundleFactor (esc (Shift.modeName c.Gas.ShiftMode))) |> ignore
        html.Append("</div><script>").Append(js).Append(data).Append(script).Append("</script></body></html>") |> ignore
        html.ToString()
