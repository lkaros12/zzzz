import bpy,numpy as np,json,math
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work');a=bpy.data.objects['Armature'];m=bpy.data.objects['remesh'];s=bpy.context.scene
for o in bpy.data.objects:
 if o.type=='MESH' and o!=m:o.hide_render=True
subs=[q for q in m.modifiers if q.type=='SUBSURF']
ref=np.load(P/'hair_reference.npz');base=ref['xyz'];mask=ref['mask'];protected=ref['protected']
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=760;s.render.resolution_y=860;s.render.resolution_percentage=100;s.render.image_settings.file_format='JPEG';s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(.65,.65,.65);s.display.shading.show_cavity=True
cam=bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.95
cases={'neutral':[], 'back_sway':[(b.name,(0,1,0),12 if b.name.endswith('_1') else 6) for b in a.pose.bones if b.name.startswith('HairBack')], 'side_sway':[(b.name,(0,1,0),(-1 if '_L_' in b.name else 1)*(4 if b.name.endswith('_1') else 2)) for b in a.pose.bones if b.name.startswith('HairSide')], 'bang_lift':[(b.name,(1,0,0),(-3 if b.name.endswith('_1') else -1.5)) for b in a.pose.bones if b.name.startswith('HairBang')], 'ahoge':[(b.name,(0,1,0),10) for b in a.pose.bones if b.name.startswith('HairAhoge')]}
edges=np.array([e.vertices[:] for e in m.data.edges]);length=np.linalg.norm(base[edges[:,0]]-base[edges[:,1]],axis=1);report={}
for name,rots in cases.items():
 for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for bn,axis,angle in rots:
  b=a.pose.bones[bn];b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion(b.bone.matrix_local.to_3x3().inverted()@Vector(axis),math.radians(angle))
 for q in subs:q.show_viewport=False
 bpy.context.view_layer.update();ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());xyz=np.array([v.co[:] for v in ev.data.vertices]);delta=np.linalg.norm(xyz-base,axis=1);ratio=np.linalg.norm(xyz[edges[:,0]]-xyz[edges[:,1]],axis=1)/np.maximum(length,1e-8)
 report[name]={'protected_max_displacement':float(delta[protected].max()),'unweighted_max_displacement':float(delta[mask<1e-8].max()),'hair_max_displacement':float(delta[mask>0].max()),'edge_stretch_p99':float(np.quantile(ratio[length>1e-5],.99)),'edge_stretch_max':float(ratio[length>1e-5].max())}
 assert np.isfinite(xyz).all();assert delta[protected].max()<1e-6;assert delta[mask<1e-8].max()<1e-6
 for q in subs:q.show_viewport=True
 bpy.context.view_layer.update()
 for view,pos in [('front',(.025,-3,.7)),('back',(.025,3,.7))]:
  if name in ('bang_lift','side_sway','ahoge') and view=='back':continue
  cam.location=pos;cam.rotation_euler=(Vector((.025,0,.65))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(P/('hair_test_'+name+'_'+view+'.jpg'));bpy.ops.render.render(write_still=True)
P.joinpath('hair_pose_tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))


