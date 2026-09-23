import bpy,math,json,hashlib,collections
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work');m=bpy.data.objects['remesh'];a=bpy.data.objects['Armature'];s=bpy.context.scene;c=.025518365
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
xyz=np.array([v.co[:] for v in m.data.vertices]);names=[g.name for g in m.vertex_groups];old=[{names[g.group]:g.weight for g in v.groups} for v in m.data.vertices]
chains={}
for label,dx in [('R2',-.18),('R1',-.09),('C',0),('L1',.09),('L2',.18)]:
 outer=abs(dx)>.15
 chains['HairBack_'+label]=[(c+dx*.65,.10 if outer else .16,.86),(c+dx*1.1,.17 if outer else .255,.72),(c+dx*1.14,.19 if outer else .255,.56),(c+dx*1.0,.15 if outer else .215,.405)]
for label,sign in [('L',1),('R',-1)]:
 chains['HairSide_'+label]=[(c+sign*.145,-.025,.85),(c+sign*.205,-.025,.73),(c+sign*.20,.005,.60),(c+sign*.19,.05,.46)]
 chains['HairBang_'+label]=[(c+sign*.045,-.075,.885),(c+sign*.09,-.12,.80),(c+sign*.135,-.115,.71),(c+sign*.145,-.07,.59)]
chains['HairBang_C']=[(c,-.075,.885),(c,-.14,.82),(c+.005,-.155,.755),(c+.035,-.145,.695)]
chains['HairAhoge']=[(c-.055,.015,.918),(c-.085,.015,.967),(c-.025,.015,.995),(c+.035,.015,.981)]
bpy.context.view_layer.objects.active=a;bpy.ops.object.mode_set(mode='EDIT');eb=a.data.edit_bones
for key,pts in chains.items():
 for j in range(3):
  b=eb.new(key+'_'+str(j+1));b.head=pts[j];b.tail=pts[j+1];b.parent=eb['Head'] if j==0 else eb[key+'_'+str(j)];b.use_connect=j>0;b.use_deform=True;b.roll=0
bpy.ops.object.mode_set(mode='OBJECT');coll=a.data.collections.new('Hair - deform controls')
for key in chains:
 for j in range(3):
  bn=key+'_'+str(j+1);coll.assign(a.data.bones[bn]);m.vertex_groups.new(name=bn)

def smooth(lo,hi,x):
 t=min(max((x-lo)/(hi-lo),0),1);return t*t*(3-2*t)

def chain_weights(key,p):
 pts=[Vector(q) for q in chains[key]];v=Vector(p);best=(1e9,0,0)
 for j in range(3):
  d=pts[j+1]-pts[j];t=min(max((v-pts[j]).dot(d)/d.length_squared,0),1);dist=(v-(pts[j]+d*t)).length_squared
  if dist<best[0]:best=(dist,j,t)
 _,j,t=best
 # Blend around joints; pin the top smoothly to Head.
 u=j+t;root=smooth(0,.8,u)
 centers=[.5,1.5,2.5]
 weights=np.maximum(0,1-np.abs(np.array(centers)-u));weights=weights/weights.sum() if weights.sum() else np.array([0,0,1])
 return {key+'_'+str(k+1):float(w)*root for k,w in enumerate(weights) if w>0},best[0]
new=[];mask=[]
for i,p in enumerate(xyz):
 x,y,z=p;dx=x-c
 back=smooth(.11,.16,y)*smooth(.375,.43,z)*(1-smooth(.84,.89,z))
 side=smooth(.12,.165,abs(dx))*smooth(.53,.595,z)*(1-smooth(.06,.14,y))*(1-smooth(.82,.875,z))*smooth(-.125,-.085,y)
 bang=(1-smooth(-.115,-.085,y))*smooth(.682,.715,z)*(1-smooth(.84,.895,z))*(1-smooth(.15,.19,abs(dx)))
 ahoge=smooth(.919,.946,z)
 zone=max([('HairBack_',back),('HairSide_',side),('HairBang_',bang),('HairAhoge',ahoge)],key=lambda q:q[1])
 amount=zone[1];w=dict(old[i]);head=w.get('Head',0);hair={}
 if amount>1e-6 and head>0:
  candidates=[]
  for key in chains:
   if key.startswith(zone[0]):
    cw,d=chain_weights(key,p);candidates.append((d,key,cw))
  candidates.sort();selected=candidates[:2] if zone[0] not in ('HairSide_','HairAhoge') else candidates[:1]
  scores=[1/(d+.0004)**2 for d,key,cw in selected];total=sum(scores)
  for score,(_,key,cw) in zip(scores,selected):
   for bn,val in cw.items():hair[bn]=hair.get(bn,0)+val*score/total*amount*head
  w['Head']=head-sum(hair.values());w.update(hair)
  w=dict(sorted(((n,v) for n,v in w.items() if v>1e-7),key=lambda nv:nv[1],reverse=True)[:4]);total=sum(w.values());w={n:v/total for n,v in w.items()}
 new.append(w);mask.append(sum(v for n,v in w.items() if n.startswith('Hair')))
# Identical-position seams must remain closed. Non-hair weights are untouched off-mask.
seams=collections.defaultdict(list)
for i,p in enumerate(xyz):seams[tuple(round(float(t),6) for t in p)].append(i)
for ids in seams.values():
 if len(ids)>1 and any(mask[i]>0 for i in ids):
  avg=collections.defaultdict(float)
  for i in ids:
   for n,val in new[i].items():avg[n]+=val/len(ids)
  avg=dict(sorted(avg.items(),key=lambda nv:nv[1],reverse=True)[:4]);total=sum(avg.values());avg={n:v/total for n,v in avg.items()}
  for i in ids:new[i]=avg;mask[i]=sum(v for n,v in avg.items() if n.startswith('Hair'))
changed=[i for i,w in enumerate(new) if mask[i]>1e-8]
for g in m.vertex_groups:g.remove(changed)
for i in changed:
 for n,v in new[i].items():m.vertex_groups[n].add([i],v,'REPLACE')
assert all(abs(sum(new[i].values())-1)<1e-5 for i in changed)
assert np.array_equal(xyz,np.array([v.co[:] for v in m.data.vertices]))
protected=(xyz[:,2]<.375)|((abs(xyz[:,0]-c)<.105)&(xyz[:,1]<.04)&(xyz[:,2]>.54)&(xyz[:,2]<.68))
assert max(np.array(mask)[protected])==0
report={'hair_bones':list(n for n in new[changed[0]] if n.startswith('Hair')),'chain_count':len(chains),'bone_count':len(chains)*3,'weighted_vertices':len(changed),'protected_face_body_max_hair_weight':float(max(np.array(mask)[protected])),'chains':chains,'max_influences':max(len(new[i]) for i in changed),'geometry_unchanged':True}
P.joinpath('hair_report.json').write_text(json.dumps(report,indent=2));np.savez(P/'hair_reference.npz',xyz=xyz,mask=np.array(mask),protected=protected)
s.frame_set(1)
for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
bpy.ops.wm.save_as_mainfile(filepath=str(P/'zombi1_hair_rigged.blend'))
bpy.ops.object.select_all(action='DESELECT');m.select_set(True);a.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(P/'zombi1_hair_rigged.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
# Inspection-only vertex colors; these are never saved to the deliverable.
attr=m.data.color_attributes.new(name='HairMaskPreview',type='FLOAT_COLOR',domain='POINT')
for i,v in enumerate(mask):attr.data[i].color=(.15+.8*v,.25+.25*v,.65*(1-v)+.05,1)
m.data.color_attributes.active_color=attr
exec(P.joinpath('hair_inspect.py').read_text(encoding='utf-8-sig').split("for name,pos")[0].split("P=Path")[1] if False else 'pass')
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=750;s.render.resolution_y=850;s.render.resolution_percentage=100;s.render.image_settings.file_format='JPEG';s.display.shading.light='STUDIO';s.display.shading.color_type='VERTEX';s.display.shading.show_cavity=True
cam=bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.95
for name,pos in [('front',(.025,-3,.7)),('back',(.025,3,.7)),('side',(3,0,.7))]:
 cam.location=pos;cam.rotation_euler=(Vector((c,0,.65))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(P/('hair_mask_'+name+'.jpg'));bpy.ops.render.render(write_still=True)
print(json.dumps({k:v for k,v in report.items() if k!='chains'}))
