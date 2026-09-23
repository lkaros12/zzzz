import bpy,math,json,numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work');a=bpy.data.objects['Armature'];m=bpy.data.objects['remesh'];s=bpy.context.scene
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=640;s.render.resolution_y=720;s.render.resolution_percentage=100;s.render.image_settings.file_format='JPEG'
s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(.65,.65,.65);s.display.shading.show_cavity=True
cam=bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.15
cases={'neutral':[], 'arms':[('LeftArm',(0,1,0),-40),('RightArm',(0,1,0),40)],'elbows':[('LeftForeArm',(0,0,1),-85),('RightForeArm',(0,0,1),85)],'legs':[('LeftUpLeg',(1,0,0),-40),('LeftLeg',(1,0,0),75),('RightUpLeg',(1,0,0),20),('RightLeg',(1,0,0),25)],'torso':[('Spine',(1,0,0),12),('Spine1',(0,0,1),15),('Spine2',(0,0,1),10)],'neck':[('Neck',(1,0,0),18)],'head':[('Head',(0,0,1),30)],'combined':[('LeftArm',(0,1,0),-30),('RightArm',(0,1,0),35),('LeftForeArm',(0,0,1),-70),('RightForeArm',(0,0,1),70),('LeftUpLeg',(1,0,0),-25),('LeftLeg',(1,0,0),50),('Head',(0,0,1),15)]}
base=np.array([v.co[:] for v in m.data.vertices]);edges=np.array([e.vertices[:] for e in m.data.edges]);length=np.linalg.norm(base[edges[:,0]]-base[edges[:,1]],axis=1);report={}
for name,rots in cases.items():
 for b in a.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for bn,axis,angle in rots:
  b=a.pose.bones[bn];b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion(b.bone.matrix_local.to_3x3().inverted()@Vector(axis),math.radians(angle))
 bpy.context.view_layer.update();ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());xyz=np.array([v.co[:] for v in ev.data.vertices]);ratio=np.linalg.norm(xyz[edges[:,0]]-xyz[edges[:,1]],axis=1)/np.maximum(length,1e-8)
 report[name]={'finite':bool(np.isfinite(xyz).all()),'edge_stretch_p99':float(np.quantile(ratio[length>1e-5],.99)),'max_displacement':float(np.linalg.norm(xyz-base,axis=1).max())}
 if name in ('arms','elbows'):
  report[name]['upper_head_drift']=float(np.linalg.norm(xyz[base[:,2]>.65]-base[base[:,2]>.65],axis=1).max())
 cam.location=(1.6,-3,.8) if name in ('legs','combined','elbows') else (0.025,-3,.5)
 cam.rotation_euler=(Vector((.025,0,.5))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(P/('test_'+name+'.jpg'));bpy.ops.render.render(write_still=True)
 assert report[name]['finite']
P.joinpath('pose_tests.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
