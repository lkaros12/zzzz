import bpy,math,json,hashlib,collections
import numpy as np
from mathutils import Vector,Matrix,Quaternion
from pathlib import Path
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work')
m=bpy.data.objects['remesh'];a=bpy.data.objects['Armature'];s=bpy.context.scene
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
coords=np.array([v.co[:] for v in m.data.vertices],dtype=np.float32)
original_hash=hashlib.sha256(coords.tobytes()).hexdigest()
old_names=[g.name for g in m.vertex_groups]
old=[{old_names[g.group]:g.weight for g in v.groups} for v in m.data.vertices]
rename={'LeftShoulder':'HeadTop','LeftArm':'LeftShoulder','LeftForeArm':'LeftArm','LeftHand':'LeftForeArm','RightShoulder':'LeftHand','RightArm':'RightShoulder','RightForeArm':'RightArm','RightHand':'RightForeArm','LeftUpLeg':'RightHand','LeftLeg':'LeftUpLeg','LeftFoot':'LeftLeg','LeftToeBase':'LeftFoot','RightLeg':'RightUpLeg','RightFoot':'RightLeg','RightToeBase':'RightFoot'}
# Original animation remains in the source file; old local axes are incompatible.
a.animation_data_clear()
for act in bpy.data.actions:act.use_fake_user=True
for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
a.data.pose_position='POSE'
# Rename in two passes to avoid collisions, including the orphan toe group.
if m.vertex_groups.get('RightUpLeg'):m.vertex_groups['RightUpLeg'].name='LegacyOrphanToe'
for oldn,new in rename.items():
 a.data.bones[oldn].name='TMP_'+new
 if m.vertex_groups.get(oldn):m.vertex_groups[oldn].name='TMP_'+new
for new in rename.values():
 a.data.bones['TMP_'+new].name=new
 if m.vertex_groups.get('TMP_'+new):m.vertex_groups['TMP_'+new].name=new
bpy.context.view_layer.objects.active=a
bpy.ops.object.mode_set(mode='EDIT')
eb=a.data.edit_bones;c=0.025518365
# Preserve the measured joint locations, average bilateral joint offsets.
chains={'Root':((c,0,0),(c,0,.08),None),'Hips':((c,-.01,.35718),(c,-.01,.384506),'Root'),'Spine':((c,-.01,.384506),(c,-.01,.415734),'Hips'),'Spine1':((c,-.01,.415734),(c,-.01,.45477),'Spine'),'Spine2':((c,-.01,.45477),(c,-.01,.51),'Spine1'),'Neck':((c,-.01,.51),(c,.0017,.56),'Spine2'),'Head':((c,.0017,.56),(c,.0017,.86),'Neck')}
for side,sgn in [('Left',1),('Right',-1)]:
 def pt(x,y,z):return(c+sgn*x,y,z)
 shoulder=pt(.062458,-.01,.51723);elbow=pt(.160048,.001709,.513325);wrist=pt(.265446,.005613,.513325)
 hip=pt(.057820,-.009139,.368514);knee=pt(.053988,-.014137,.210309);ankle=pt(.073449,.013359,.047707);toe=pt(.059,.0-.077,.025)
 chains.update({side+'Shoulder':(pt(.019518,-.01,.521132),shoulder,'Spine2'),side+'Arm':(shoulder,elbow,side+'Shoulder'),side+'ForeArm':(elbow,wrist,side+'Arm'),side+'Hand':(wrist,pt(.345,.005613,.511),side+'ForeArm'),side+'UpLeg':(hip,knee,'Hips'),side+'Leg':(knee,ankle,side+'UpLeg'),side+'Foot':(ankle,toe,side+'Leg'),side+'ToeBase':(toe,pt(.059,-.12,.025),side+'Foot')})
for name,(h,t,parent) in chains.items():
 b=eb.get(name) or eb.new(name);b.head=h;b.tail=t;b.roll=0;b.use_connect=False;b.use_deform=name!='Root'
for name,(h,t,parent) in chains.items():
 b=eb[name];b.parent=eb.get(parent) if parent else None
 if b.parent and (b.head-b.parent.tail).length<1e-6:b.use_connect=True
for b in eb:
 if b.name not in chains:b.use_deform=False
bpy.ops.object.mode_set(mode='OBJECT')
archive=a.data.collections.get('Legacy - disabled invalid finger rig') or a.data.collections.new('Legacy - disabled invalid finger rig')
for b in a.data.bones:
 if b.name not in chains:
  for coll in list(b.collections):coll.unassign(b)
  archive.assign(b)
archive.is_visible=False
# Reuse original weights after semantic remapping; remove spatially invalid influences.
valid=set(chains)-{'Root'}
for n in valid:
 if not m.vertex_groups.get(n):m.vertex_groups.new(name=n)
new=[];changed=0;preserved=0
for i,v in enumerate(m.data.vertices):
 x,y,z=v.co;side='Left' if x>=c else 'Right';opp='Right' if side=='Left' else 'Left';w=collections.defaultdict(float)
 for n,val in old[i].items():
  if n=='RightUpLeg':nn=side+'Foot'
  elif n.startswith('HandLoRA_'):nn='Head' if y>.06 or z>.565 else side+'ForeArm'
  else:nn=rename.get(n,n)
  if nn=='HeadTop':nn='Head'
  if nn in valid:w[nn]+=val
 # Hair and face cannot follow arms. Back hair descends below the shoulders.
 head_factor=max(min(max((z-.55)/.035,0),1), min(max((y-.055)/.035,0),1)*min(max((z-.375)/.035,0),1))
 if head_factor>=1:w={'Head':1.0}
 else:
  for n in list(w):
   if (z<.28 and (n.startswith(opp) or n in ('Head','Neck') or 'Arm' in n or 'Hand' in n or 'Shoulder' in n)) or (z>.43 and ('Leg' in n or 'Foot' in n or 'Toe' in n)):
    del w[n]
  # Keep head influence off the exposed hands and forearms.
  if abs(x-c)>.24 and y<.06 and z<.55:w.pop('Head',None);w.pop('Neck',None)
  if z<.38:w.pop('Head',None);w.pop('Neck',None)
  if not w:w={side+'Foot' if z<.08 else side+'Leg' if z<.22 else 'Hips':1.0}
  total=sum(w.values());w={n:val/total*(1-head_factor) for n,val in w.items()}
  w['Head']=w.get('Head',0)+head_factor
 if side+'Foot' in w and z<.075:
  toe_mix=min(max((-y-.06)/.05,0),1)*min(max((.075-z)/.025,0),1)
  w[side+'ToeBase']=w.get(side+'ToeBase',0)+w[side+'Foot']*toe_mix
  w[side+'Foot']*=1-toe_mix
 w=dict(sorted(((n,val) for n,val in w.items() if val>1e-5),key=lambda nv:nv[1],reverse=True)[:4]);total=sum(w.values());w={n:val/total for n,val in w.items()}
 new.append(w)
# Same-position split vertices receive identical skinning to prevent opening seams.
seams=collections.defaultdict(list)
for v in m.data.vertices:seams[tuple(round(float(x),6) for x in v.co)].append(v.index)
for ids in seams.values():
 if len(ids)>1:
  mean=collections.defaultdict(float)
  for i in ids:
   for n,val in new[i].items():mean[n]+=val/len(ids)
  mean=dict(sorted(mean.items(),key=lambda nv:nv[1],reverse=True)[:4]);total=sum(mean.values());mean={n:val/total for n,val in mean.items()}
  for i in ids:new[i]=mean
for g in m.vertex_groups:g.remove(list(range(len(m.data.vertices))))
for i,w in enumerate(new):
 for n,val in w.items():m.vertex_groups[n].add([i],val,'REPLACE')
# Hard invariants: topology/coordinates unchanged, skinning normalized, hierarchy present.
assert original_hash==hashlib.sha256(np.array([v.co[:] for v in m.data.vertices],dtype=np.float32).tobytes()).hexdigest()
assert all(abs(sum(w.values())-1)<1e-6 and len(w)<=4 for w in new)
assert all(n in a.data.bones for n in chains)
for side in ('Left','Right'):assert a.data.bones[side+'UpLeg'].parent.name=='Hips'
a.show_in_front=True;a.data.display_type='OCTAHEDRAL';s.frame_set(1)
for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
report={'mesh_vertices':len(coords),'mesh_coordinate_sha256':original_hash,'mesh_geometry_unchanged':True,'rename_map':rename,'added_bones':['LeftToeBase','RightToeBase'],'disabled_legacy_bones':[b.name for b in a.data.bones if b.name not in chains],'max_influences':4,'normalized':True,'source_animation_detached':True}
P.joinpath('repair_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(P/'zombi1_humanoid.blend'))
bpy.ops.object.select_all(action='DESELECT');m.select_set(True);a.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(P/'zombi1_humanoid.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
print('REPAIR',json.dumps(report))


