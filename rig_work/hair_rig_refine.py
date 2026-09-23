from pathlib import Path
source=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work\hair_rig.py').read_text(encoding='utf-8-sig')
exec(source.split('new=[];mask=[]')[0])
new=[];mask=[]
protected=(xyz[:,2]<.375)|((abs(xyz[:,0]-c)<.105)&(xyz[:,1]<.04)&(xyz[:,2]>.54)&(xyz[:,2]<.68))
protected|=np.array([w.get('Head',0)<.95 for w in old])&(xyz[:,2]<.575)&(xyz[:,1]<.12)
protected|=(abs(xyz[:,0]-c)>.29)&(xyz[:,2]<.57)
for i,p in enumerate(xyz):
 x,y,z=p;dx=x-c
 back=smooth(.05,.105,y)*smooth(.37,.395,z)*(1-smooth(.84,.90,z))
 if abs(dx)<.14 and z<.56:back*=smooth(.105,.145,y)
 side=(1-smooth(.245,.285,abs(dx)))*smooth(.10,.15,abs(dx))*smooth(.515,.555,z)*(1-smooth(.11,.18,y))*(1-smooth(.82,.885,z))*smooth(-.145,-.11,y)
 bang=(1-smooth(-.115,-.09,y))*smooth(.68,.70,z)*(1-smooth(.84,.90,z))*(1-smooth(.145,.19,abs(dx)))
 ahoge=smooth(.919,.94,z)
 amount=max(back,side,bang,ahoge)
 if protected[i]:amount=0
 hair={};w=dict(old[i])
 if amount>1e-7:
  candidates=[]
  for key in chains:
   if (key=='HairAhoge') != (z>.918):continue
   cw,d=chain_weights(key,p);candidates.append((d,key,cw))
  candidates.sort();selected=candidates[:2] if z<.918 else candidates[:1]
  scores=[1/(d+.001)**2 for d,key,cw in selected];total=sum(scores)
  for score,(_,key,cw) in zip(scores,selected):
   for bn,val in cw.items():hair[bn]=hair.get(bn,0)+val*score/total*amount
  w={n:v*(1-amount) for n,v in old[i].items()}
  w['Head']=w.get('Head',0)+amount-sum(hair.values());w.update(hair)
 new.append(w);mask.append(amount)
# Smooth skinning across the existing surface without editing mesh topology.
allnames=list(dict.fromkeys([n for w in new for n in w]));ind={n:j for j,n in enumerate(allnames)}
W=np.zeros((len(new),len(allnames)),dtype=np.float32)
for i,w in enumerate(new):
 for n,v in w.items():W[i,ind[n]]=v
# Virtual welding only for weight calculations, never for the mesh.
keys={};uid=[]
for p in xyz:
 key=tuple(round(float(v),6) for v in p)
 if key not in keys:keys[key]=len(keys)
 uid.append(keys[key])
uid=np.array(uid);count=np.bincount(uid);U=np.zeros((len(keys),len(allnames)),dtype=np.float32);np.add.at(U,uid,W);U/=count[:,None]
active=np.zeros(len(keys),dtype=bool);active[uid[np.array(mask)>1e-7]]=True
edges=np.array([e.vertices[:] for e in m.data.edges]);edges=np.unique(np.sort(uid[edges],axis=1),axis=0);edges=edges[edges[:,0]!=edges[:,1]]
deg=np.bincount(edges.ravel(),minlength=len(keys)).astype(np.float32)
for step in range(14):
 sums=np.zeros_like(U);np.add.at(sums,edges[:,0],U[edges[:,1]]);np.add.at(sums,edges[:,1],U[edges[:,0]])
 avg=sums/np.maximum(deg[:,None],1);U[active]=.45*U[active]+.55*avg[active]
W=U[uid];mask=[];new=[]
for i,row in enumerate(W):
 if not active[uid[i]]:w=old[i]
 else:
  ids=np.argsort(row)[-8:];w={allnames[j]:float(row[j]) for j in ids if row[j]>1e-6};total=sum(w.values());w={n:v/total for n,v in w.items()}
 new.append(w);mask.append(sum(v for n,v in w.items() if n.startswith('Hair')))
# Original face/body guard is hard, including after smoothing.
for i in np.flatnonzero(protected):new[i]=old[i];mask[i]=0
source=source.replace("'max_influences':max(len(new[i]) for i in changed)","'max_influences':max(len(new[i]) for i in changed)")
source=source.replace("for name,pos in [('front'", "for o in bpy.data.objects:\n if o.type=='MESH' and o!=m:o.hide_render=True\nfor name,pos in [('front'")
exec(source[source.index('changed=[i for i,w'):])

