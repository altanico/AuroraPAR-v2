# Generates the illustrations of the user manual (docs/images/*.svg).
# Usage: python3 tools/make_manual_images.py docs/images
import math, os, sys
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
FONT = "Segoe UI, Helvetica, Arial, sans-serif"
MONO = "Consolas, Menlo, monospace"
GREEN="#00a000"; YEL="#ffff00"; RED="#ff2020"; BLUE="#5f9ea0"; TXT="#e8e8e8"; CALL="#ffb000"

class S:
    def __init__(s, w, h, bg=None):
        s.w, s.h, s.items = w, h, []
        if bg: s.rect(0,0,w,h,fill=bg)
    def add(s, x): s.items.append(x)
    def line(s,x1,y1,x2,y2,c,w=1,dash=None,op=1):
        d=f' stroke-dasharray="{dash}"' if dash else ''
        s.add(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{c}" stroke-width="{w}"{d} opacity="{op}"/>')
    def rect(s,x,y,w,h,fill="none",stroke="none",sw=1,rx=0,op=1):
        s.add(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" opacity="{op}"/>')
    def circle(s,x,y,r,fill="none",stroke="none",sw=1,op=1):
        s.add(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" opacity="{op}"/>')
    def text(s,x,y,t,c=TXT,size=12,anchor="start",weight="normal",font=FONT,op=1):
        t=t.replace("&","&amp;").replace("<","&lt;")
        s.add(f'<text x="{x:.1f}" y="{y:.1f}" fill="{c}" font-size="{size}" font-family="{font}" text-anchor="{anchor}" font-weight="{weight}" opacity="{op}">{t}</text>')
    def path(s,d,fill="none",stroke="none",sw=1,op=1):
        s.add(f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" opacity="{op}"/>')
    def callout(s,n,x,y):
        s.circle(x,y,10,fill=CALL)
        s.text(x,y+4.5,str(n),c="#000",size=12,anchor="middle",weight="bold")
    def note(s,x,y,tx,ty,label,anchor="start"):
        s.line(x,y,tx,ty,CALL,1)
        s.circle(x,y,2.5,fill=CALL)
        s.text(tx+(4 if anchor=="start" else -4),ty+4,label,c=CALL,size=12,anchor=anchor)
    def save(s,name):
        body="\n".join(s.items)
        open(os.path.join(OUT,name),"w").write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{s.w}" height="{s.h}" viewBox="0 0 {s.w} {s.h}">\n{body}\n</svg>\n')

def crosscircle(s,x,y,r,c):
    s.circle(x,y,r,stroke=c,sw=2)
    s.line(x-r,y,x+r,y,c,2); s.line(x,y-r,x,y+r,c,2)

def elevation(s, x0, y0, W, H, labels=True, track=True, dots=True, info=True, rng=10, marks=None, rmark=None, tdist=6.2, step=0.3):
    """PAR elevation view in the box (x0,y0,W,H), antenna on the left, true angles on a common scale."""
    s.rect(x0,y0,W,H,fill="#000")
    g = y0+H-24
    ax = x0+26; endx = x0+W-30
    nm = (endx-ax)/(rng+0.6)
    thr = ax+0.45*nm; td = ax+0.6*nm
    V = (g-(y0+16))/((endx-ax)*math.tan(math.radians(8)))   # vertical exaggeration
    def hy(xfrom, x, ang): return g-(x-xfrom)*math.tan(math.radians(ang))*V
    gp = lambda d,off=0: hy(td, td+d*nm, 3+off)
    s.line(x0,g,thr,g,GREEN,3); s.line(thr,g,x0+W,g,GREEN,2)
    s.line(thr,g,thr,g-9,GREEN,3)
    s.line(ax,g,endx,hy(ax,endx,8),BLUE,3)
    for off,c,w in [(0,YEL,2),(0.5,RED,1),(-0.5,RED,1)]:
        s.line(td,g,endx,hy(td,endx,3+off),c,w)
    xi = td+0.75*nm; dh_y = gp(0.75)
    s.line(td,dh_y,td+3*nm,dh_y,RED,2)
    s.line(xi,g,xi,dh_y,RED,2,dash="4 3")
    s.line(td,g,td,g-11,YEL,2)
    s.rect(ax-4,g-8,8,8,fill=BLUE)
    for i in range(1,rng+1):
        x = td+i*nm
        if x>endx+1: break
        s.line(x,g,x,hy(ax,x,8),GREEN,1)
        if marks is None or i in marks:
            s.text(x,g-4,f"{i}NM",c=YEL,size=10,anchor="middle")
    pos=None
    if track:
        d=tdist; tx=td+d*nm; ty=hy(td,tx,3.9)
        if dots:
            for j in range(1,10):
                xx=tx+j*step*nm; s.circle(xx, hy(td,xx,3.9+0.03*j), 1.7, fill=RED)
        crosscircle(s,tx,ty,6,RED)
        if labels:
            lx,ly=tx+14,ty-70
            for n,t in enumerate(["U 115 ft    AZA123",f"{d:.1f} NM","A 2060 ft","140 Kts","-750 ft/min"]):
                s.text(lx,ly+n*13,t,c="#fff",size=10.5)
        pos=(tx,ty)
    if info:
        for n,(t,c) in enumerate([("RWY 16L","#fff"),("CRS 163","#fff"),("GP 3.0°","#fff"),("QNH 1013","#fff"),("DA 392 ft","#fff"),("STS OK",GREEN),("DATA 0.5s",GREEN)]):
            s.text(x0+40, y0+16+n*13, t, c=c, size=10.5)
    return dict(g=g,ax=ax,td=td,thr=thr,nm=nm,endx=endx,dh=dh_y,xi=xi,track=pos,gp=gp,hy=hy)

def azimuth(s, x0, y0, W, H, labels=True, rng=10, tdist=6.2, step=0.3):
    s.rect(x0,y0,W,H,fill="#000")
    cy=y0+H/2; ax=x0+26; endx=x0+W-30
    nm=(endx-ax)/(rng+0.6); thr=ax+0.45*nm; td=ax+0.6*nm
    V=(H/2-14)/((endx-ax)*math.tan(math.radians(10)))
    off=lambda xfrom,x,ang: (x-xfrom)*math.tan(math.radians(ang))*V
    s.line(x0,cy,thr,cy,GREEN,3); s.line(thr,cy-7,thr,cy+7,GREEN,3)
    s.line(ax,cy,endx,cy-off(ax,endx,10),BLUE,3); s.line(ax,cy,endx,cy+off(ax,endx,10),BLUE,3)
    s.line(td,cy,thr+1,cy,YEL,2,dash="4 3"); s.line(thr,cy,endx,cy,YEL,2)
    s.line(td,cy,endx,cy-off(td,endx,1.5),RED,1); s.line(td,cy,endx,cy+off(td,endx,1.5),RED,1)
    s.line(td,cy-8,td,cy+8,YEL,2)
    s.rect(ax-4,cy-4,8,8,fill=BLUE)
    for i in range(1,rng+1):
        x=td+i*nm
        if x>endx+1: break
        h=off(ax,x,10); s.line(x,cy-h,x,cy+h,GREEN,1)
    xi=td+0.75*nm; h=off(ax,xi,10); s.line(xi,cy-h,xi,cy+h,RED,2)
    d=tdist; tx=td+d*nm; ty=cy-off(td,tx,0.6)
    for j in range(1,10):
        xx=tx+j*step*nm; s.circle(xx, cy-off(td,xx,0.6+0.03*j), 1.7, fill=GREEN)
    crosscircle(s,tx,ty,6,GREEN)
    if labels:
        for n,t in enumerate(["R 35 ft    AZA123",f"{d:.1f} NM","140 Kts"]):
            s.text(tx+14,ty-44+n*13,t,c="#fff",size=10.5)
    return dict(cy=cy,ax=ax,td=td,thr=thr,nm=nm,endx=endx,track=(tx,ty),xi=xi,off=off)

# ---------- 1. main window ----------
s=S(900,560)
s.rect(0.5,0.5,899,559,fill="#2b2b2b",stroke="#555",rx=6)
s.rect(1,1,898,30,fill="#3a3a3a",rx=6); s.rect(1,20,898,11,fill="#3a3a3a")
s.text(14,21,"Aurora PAR",c="#ddd",size=13)
for i,c in enumerate(["#888","#888","#c44"]): s.text(830+i*22,21,"–□×"[i],c="#ccc",size=13)
DX,DY,DW,DH=8,38,744,514
elevation(s,DX,DY,DW,DH/2-1)
azimuth(s,DX,DY+DH/2+1,DW,DH/2-1)
# right column
cx=760; cw=132; y=40
def ctl(t,h=26,kind="btn"):
    global y
    if kind=="combo":
        s.rect(cx,y,cw,h,fill="#f3f3f3",stroke="#999",rx=2); s.text(cx+8,y+17,t,c="#111",size=12); s.text(cx+cw-14,y+17,"▾",c="#333",size=12)
    elif kind=="label":
        s.text(cx+2,y+14,t,c="#ddd",size=11.5); h=18
    else:
        s.rect(cx,y,cw,h,fill="#e1e1e1",stroke="#999",rx=2); s.text(cx+cw/2,y+17,t,c="#111",size=12,anchor="middle")
    top=y; y+=h+5; return top
r1=ctl("LIRF 16L",kind="combo"); r2=ctl("10",kind="combo"); r3=ctl("Settings..."); r4=ctl("Runways...")
y+=6; r5=ctl("DA (ft)",kind="label")
s.rect(cx,y,26,24,fill="#e1e1e1",stroke="#999"); s.text(cx+13,y+17,"−",c="#111",size=14,anchor="middle")
s.rect(cx+30,y,cw-60,24,fill="#fff",stroke="#999"); s.text(cx+cw/2,y+17,"200",c="#111",size=12,anchor="middle")
s.rect(cx+cw-26,y,26,24,fill="#e1e1e1",stroke="#999"); s.text(cx+cw-13,y+17,"+",c="#111",size=14,anchor="middle"); y+=34
r6=ctl("Antenna tilt",kind="label")
s.rect(cx,y,cw/2-1,24,fill="#e1e1e1",stroke="#999"); s.text(cx+cw/4,y+16,"EL ▲",c="#111",size=11.5,anchor="middle")
s.rect(cx+cw/2+1,y,cw/2-1,24,fill="#e1e1e1",stroke="#999"); s.text(cx+3*cw/4,y+16,"EL ▼",c="#111",size=11.5,anchor="middle"); y+=27
s.rect(cx,y,cw/2-1,24,fill="#e1e1e1",stroke="#999"); s.text(cx+cw/4,y+16,"AZ L",c="#111",size=11.5,anchor="middle")
s.rect(cx+cw/2+1,y,cw/2-1,24,fill="#e1e1e1",stroke="#999"); s.text(cx+3*cw/4,y+16,"AZ R",c="#111",size=11.5,anchor="middle"); y+=27
ctl("Neutral",h=24); y+=8
r7=ctl("Hide labels (L)"); y+=6; r8=ctl("Analog (A)")
# callouts
s.callout(1,DX+24,DY+20)
s.callout(2,DX+DW-60,DY+40)
s.callout(3,DX+DW-60,DY+DH/2+40)
for n,yy in [(4,r1+13),(5,r2+13),(6,r3+13),(7,r4+13),(8,r5+44),(9,r6+40),(10,r7+13),(11,r8+13)]:
    s.callout(n,cx-14,yy)
s.save("main-window.svg")

# ---------- 2. elevation with numbers ----------
s=S(760,360,bg="#1b1b1b")
e=elevation(s,10,10,740,340,info=False,rng=5,tdist=3.2,step=0.15)
g=e["g"]; hy=e["hy"]; nm=e["nm"]; td=e["td"]; ax=e["ax"]
s.callout(1,ax,g+13)
s.callout(2,td+4.3*nm,hy(ax,td+4.3*nm,8)-16)
s.callout(3,td+4.7*nm,hy(td,td+4.7*nm,3)+15)
s.callout(4,td+4.7*nm,hy(td,td+4.7*nm,3.5)-14)
tx,ty=e["track"]; s.callout(5,tx-16,ty+12)
s.callout(6,tx+1.1*nm,hy(td,tx+1.1*nm,4.0)-16)
s.callout(7,tx+135,ty-80)
s.callout(8,td+2.2*nm,e["dh"]-14)
s.callout(9,e["xi"]+14,g-34)
s.callout(10,td+2*nm+14,hy(ax,td+2*nm,8)+30)
s.callout(11,td+8,g+13)
s.callout(12,e["thr"]-30,g+13)
s.save("elevation-view.svg")

# ---------- 3. azimuth with numbers ----------
s=S(760,280,bg="#1b1b1b")
a=azimuth(s,10,10,740,260,rng=5,tdist=3.2,step=0.15)
cy=a["cy"]; off=a["off"]; nm=a["nm"]; td=a["td"]
s.callout(1,a["ax"],cy+16)
s.callout(2,td+4.2*nm,cy-off(a["ax"],td+4.2*nm,10)-14)
s.callout(3,td+4.6*nm,cy-14)
s.callout(4,td+4.6*nm,cy+off(td,td+4.6*nm,1.5)+14)
tx,ty=a["track"]; s.callout(5,tx-16,ty+14)
s.callout(6,a["xi"]+16,cy+40)
s.callout(7,td,cy+22)
s.text(a["endx"]-10,30,"pilot's RIGHT",c="#aaa",size=11,anchor="end")
s.text(a["endx"]-10,262,"pilot's LEFT",c="#aaa",size=11,anchor="end")
s.save("azimuth-view.svg")

# ---------- 4. analog console ----------
SEG={' ':'','0':'abcdefkl','1':'bc','2':'abdegh','3':'abcdh','4':'bcfgh','5':'acdfgh','6':'acdefgh','7':'abc','8':'abcdefgh','9':'abcdfgh',
 'A':'abcefgh','B':'abcdhjm','C':'adef','D':'abcdjm','E':'adefg','F':'aefg','G':'acdefh','H':'bcefgh','I':'adjm','L':'def','N':'bcefin','O':'abcdef','P':'abefgh','R':'abefghn','S':'acdfgh','T':'ajm','U':'bcdef','-':'gh'}
def seg_display(s,x,y,text,cells=5,ch=18,cw=10.5,gap=4.5,t=1.7,on="#ffa830",ghost="#2a1c0c"):
    W=2*5+cells*cw+(cells-1)*gap+2; H=ch+10
    s.rect(x,y,W,H,fill="#0a0705",stroke="#121210",sw=1.5,rx=2)
    cl=[]
    for c in text.upper():
        if c=='.' and cl and not cl[-1][1]: cl[-1]=(cl[-1][0],True); continue
        cl.append((c,False))
    cl=cl[:cells]
    while len(cl)<cells: cl.insert(0,(' ',False))
    for i,(c,pt) in enumerate(cl):
        left=x+6+i*(cw+gap); top=y+5; lit=SEG.get(c,''); w=cw; h=ch; mid=h/2; g=t*0.9; sl=0.12
        P=lambda px,py:(left+px+(h-py)*sl-h*sl/2, top+py)
        def bar(nm,a,b,diag=False):
            col=on if nm in lit else ghost
            s.line(a[0],a[1],b[0],b[1],col,t*(0.85 if diag else 1))
        bar('a',P(g,0),P(w-g,0));bar('b',P(w,g),P(w,mid-g));bar('c',P(w,mid+g),P(w,h-g));bar('d',P(g,h),P(w-g,h))
        bar('e',P(0,mid+g),P(0,h-g));bar('f',P(0,g),P(0,mid-g));bar('g',P(g,mid),P(w/2-g*.6,mid));bar('h',P(w/2+g*.6,mid),P(w-g,mid))
        bar('j',P(w/2,g*1.2),P(w/2,mid-g*1.2));bar('m',P(w/2,mid+g*1.2),P(w/2,h-g*1.2))
        dx,dy=g*1.1,g*1.5
        bar('i',P(dx,dy),P(w/2-dx,mid-dy),1);bar('k',P(w-dx,dy),P(w/2+dx,mid-dy),1);bar('l',P(dx,h-dy),P(w/2-dx,mid+dy),1);bar('n',P(w-dx,h-dy),P(w/2+dx,mid+dy),1)
        px,py=P(w+gap/2+.5,h); s.circle(px,py,t*0.65,fill=on if pt else ghost)
    return W
def lamp(s,x,y,col):
    s.circle(x,y,9,fill="#9a9c96",stroke="#181816")
    s.add(f'<circle cx="{x}" cy="{y}" r="7" fill="{col}"/>')
def knob(s,x,y,title,labels,pointer):
    s.text(x,y-34,title,c="#d8d8d0",size=10,anchor="middle",weight="bold")
    n=len(labels)
    for i,l in enumerate(labels):
        a=math.radians(-135+270*i/(n-1)) if n>1 else 0
        s.line(x+20*math.sin(a),y-20*math.cos(a),x+24*math.sin(a),y-24*math.cos(a),"#d8d8d0",1)
        if l: s.text(x+32*math.sin(a),y-32*math.cos(a)+3,l,c="#d8d8d0",size=8.5,anchor="middle")
    s.add(f'<circle cx="{x}" cy="{y}" r="17" fill="url(#knob)" stroke="#101010"/>')
    s.add(f'<circle cx="{x}" cy="{y}" r="12" fill="url(#cap)" stroke="#101010"/>')
    a=math.radians(pointer); s.line(x+4*math.sin(a),y-4*math.cos(a),x+15*math.sin(a),y-15*math.cos(a),"#fff",2.5)

PH="#a8ff60"
s=S(900,520)
s.add('<defs><radialGradient id="glass" cx="50%" cy="50%" r="50%"><stop offset="0" stop-color="#162412"/><stop offset="0.7" stop-color="#0c160a"/><stop offset="1" stop-color="#050904"/></radialGradient>'
      '<linearGradient id="ring" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#c4c6c0"/><stop offset="0.45" stop-color="#80837d"/><stop offset="1" stop-color="#3e403b"/></linearGradient>'
      '<radialGradient id="knob" cx="35%" cy="30%" r="70%"><stop offset="0" stop-color="#787a76"/><stop offset="1" stop-color="#262725"/></radialGradient>'
      '<radialGradient id="cap" cx="35%" cy="30%" r="70%"><stop offset="0" stop-color="#555753"/><stop offset="1" stop-color="#1a1b19"/></radialGradient>'
      '<radialGradient id="vig" cx="50%" cy="50%" r="50%"><stop offset="0.62" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity="0.6"/></radialGradient>'
      f'<clipPath id="scope"><circle cx="505" cy="260" r="228"/></clipPath>'
      '<filter id="glow"><feGaussianBlur stdDeviation="2.2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>')
s.rect(0,0,900,520,fill="#2e302c",rx=6)
# console panel
s.text(98,22,"PRECISION APPROACH RADAR",c="#8c8e88",size=9,anchor="middle",weight="bold")
rows=[("APT","LIRF"),("RWY","16L"),("CRS","163"),("GP DEG","3.0"),("QNH","1013"),("DA FT","392"),("RANGE NM","10"),("EL TILT","0.0"),("AZ TILT","R2.0")]
for i,(lab,val) in enumerate(rows):
    yy=36+i*34
    s.text(12,yy+19,lab,c="#d8d8d0",size=11,weight="bold")
    seg_display(s,103,yy,val)
s.line(12,352,184,352,"#1a1b18",1)
for i,(lab,col) in enumerate([("STS","#70ff60"),("ANT. R/R","#70ff60"),("TILT","#ffb020")]):
    yy=374+i*26; lamp(s,22,yy,col); s.text(40,yy+4,lab,c="#d8d8d0",size=11,weight="bold")
s.line(196,0,196,520,"#1a1b18",1)
# scope
cx,cy,R,ring=505,260,228,9
s.circle(cx,cy,R+ring+3,stroke="#000",sw=3,op=0.5)
s.add(f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="url(#glass)"/>')
s.add(f'<g filter="url(#glow)" clip-path="url(#scope)">')
# elevation (top half) and azimuth (bottom half) in phosphor
x0=cx-R+0.12*2*R; x1=cx+R-0.04*2*R; gy=cy-6
def ph(o): return f'{PH}'
s.line(x0-20,gy,x1,gy,PH,2,op=0.6); s.line(x0,gy,x1-20,cy-R*0.75,PH,2.5,op=0.55)
s.line(x0+12,gy,x1-10,gy-62,PH,2,op=0.9); s.line(x0+12,gy,x1-10,gy-80,PH,1,op=0.45); s.line(x0+12,gy,x1-10,gy-45,PH,1,op=0.45)
for i in range(1,11):
    xx=x0+12+i*(x1-x0-22)/10.5
    top=gy-(gy-(cy-R*0.75))*(xx-x0)/(x1-20-x0)
    if top<cy-math.sqrt(max(R*R-(xx-cx)**2,0))+4: continue
    s.line(xx,gy,xx,top,PH,1,op=0.45)
ay=cy+R*0.42
s.line(x0-20,ay,x1,ay,PH,2,op=0.9)
s.line(x0,ay,x1-30,ay-R*0.36,PH,2.5,op=0.55); s.line(x0,ay,x1-30,ay+R*0.36,PH,2.5,op=0.55)
s.line(x0+12,ay,x1-20,ay-30,PH,1,op=0.45); s.line(x0+12,ay,x1-20,ay+30,PH,1,op=0.45)
# beam and echoes
s.line(x0,gy,x1-30,gy-140,"#d8ffb0",2,op=0.9)
s.add(f'<ellipse cx="{x0+250}" cy="{gy-52}" rx="5" ry="2.2" fill="{PH}"/>')
for j in range(1,7): s.circle(x0+250+j*9,gy-52-j*1.2,1.5,fill=PH,op=0.5-0.06*j)
s.add(f'<ellipse cx="{x0+250}" cy="{ay-4}" rx="5" ry="2.2" fill="{PH}" opacity="0.45"/>')
s.add('</g>')
s.add(f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="url(#vig)"/>')
s.add(f'<circle cx="{cx}" cy="{cy}" r="{R+ring/2}" fill="none" stroke="url(#ring)" stroke-width="{ring}"/>')
for i in range(8):
    a=math.radians(22.5+45*i); px,py=cx+(R+ring/2)*math.sin(a),cy-(R+ring/2)*math.cos(a)
    s.circle(px,py,2.4,fill="#bdbdb6",stroke="#222")
for sg in (-1,1): s.rect(cx-5,cy+sg*R-3,10,6,fill="#6a6c66",stroke="#1a1b18")
# knobs column
s.line(800,0,800,520,"#1a1b18",1)
knob(s,850,80,"RANGE NM",["1","2.5","5","10","15","20"],-27)
knob(s,850,180,"EL TILT",["DN","","","","","0","","","","","UP"],0)
knob(s,850,280,"AZ TILT",["L","","","","","0","","","","","R"],27)
knob(s,850,380,"DH",[],40)
s.rect(812,450,76,26,fill="#e1e1e1",stroke="#999",rx=2); s.text(850,467,"Modern (A)",c="#111",size=11,anchor="middle")
s.save("analog-console.svg")

# ---------- 5. horizon options ----------
s=S(860,170,bg="#1b1b1b")
for k,(title,textbelow) in enumerate([("Distance text above, markers below",False),("Distance text below, markers above",True)]):
    x0=10+k*430; W=410
    s.rect(x0,30,W,130,fill="#000")
    s.text(x0+W/2,20,title,c="#ddd",size=12,anchor="middle")
    g=30+130-30
    s.line(x0,g,x0+W,g,GREEN,2)
    for i in range(1,5):
        x=x0+20+i*90
        s.line(x,g,x,40,GREEN,1)
        if i==3:
            s.line(x,g,x,40,"#ff8000",2,dash="6 3")
        if textbelow: s.text(x,g+16,f"{i}NM",c=YEL,size=11,anchor="middle")
        else: s.text(x,g-5,f"{i}NM",c=YEL,size=11,anchor="middle")
    mx=x0+20+3*90; my=(g-14) if textbelow else (g+14)
    s.path(f"M{mx},{my+7} L{mx+7},{my-6} L{mx-7},{my-6} Z",fill="#ff8000")
s.save("horizon-options.svg")
print("ok")
