import bpy,json,numpy as np,math
from pathlib import Path
from mathutils import Matrix,Quaternion,Vector
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work');a=bpy.data.objects['Armature'];m=bpy.data.objects['remesh'];ref=np.load(P/'hair_reference.npz');base=ref['xyz'];mask=ref['mask'];outside=mask<1e-8
subs=[q for q in m.modifiers if q.type=='SUBSURF'];states=[q.show_viewport for q in subs]
for q in subs:q.show_viewport=False
results={}
for b in a.pose.bones:
 if not b.name.startswith('Hair'):continue
 b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion((1,0,0),math.radians(5));bpy.context.view_layer.update()
 ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());xyz=np.array([v.co[:] for v in ev.data.vertices]);d=np.linalg.norm(xyz-base,axis=1)
 assert np.isfinite(xyz).all();assert d[outside].max()<1e-6;assert d.max()>1e-6
 results[b.name]={'outside_hair_max_displacement':float(d[outside].max()),'hair_max_displacement':float(d.max())};b.matrix_basis=Matrix.Identity(4)
for q,state in zip(subs,states):q.show_viewport=state
bpy.context.view_layer.update()
P.joinpath('hair_individual_tests.json').write_text(json.dumps(results,indent=2))
report=json.loads(P.joinpath('hair_report.json').read_text());report['hair_bones']=list(results);P.joinpath('hair_report.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT');a.select_set(True);bpy.context.view_layer.objects.active=a
for b in a.pose.bones:b.select=False
a.pose.bones['HairBack_C_1'].select=True;a.data.bones.active=a.data.bones['HairBack_C_1']
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.wm.save_as_mainfile(filepath=str(P/'zombi1_hair_rigged.blend'))
print('Individual hair control checks:',len(results),'PASS')

