import bpy,hashlib,json,numpy as np
from pathlib import Path
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work')
def signature():
 m=bpy.data.objects['remesh'].data
 return {'vertices':len(m.vertices),'polygons':len(m.polygons),'coords':hashlib.sha256(np.array([v.co[:] for v in m.vertices],dtype=np.float32).tobytes()).hexdigest(),'topology':hashlib.sha256(str([tuple(p.vertices) for p in m.polygons]).encode()).hexdigest(),'uv':[(u.name,hashlib.sha256(np.array([v.uv[:] for v in u.data],dtype=np.float32).tobytes()).hexdigest()) for u in m.uv_layers],'materials':[x.name for x in m.materials]}
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
before=signature()
bpy.ops.wm.open_mainfile(filepath=str(P/'zombi1_humanoid.blend'))
after=signature();assert before==after,(before,after)
a=bpy.data.objects['Armature'];c=a.data.bones['Hips'].head_local.x
sym={}
for b in a.data.bones:
 if b.name.startswith('Left'):
  r=a.data.bones.get('Right'+b.name[4:])
  if r:
   h=r.head_local.copy();t=r.tail_local.copy();h.x=2*c-h.x;t.x=2*c-t.x;sym[b.name]=max((h-b.head_local).length,(t-b.tail_local).length)
assert max(sym.values())<1e-6
report={'original':before,'repaired':after,'geometry_topology_uv_materials_identical':True,'symmetry_max_error':max(sym.values())}
P.joinpath('preservation_check.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
