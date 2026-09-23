import bpy,numpy as np,json,hashlib
from pathlib import Path
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work')
def capture():
 result={}
 for o in bpy.data.objects:
  if o.type=='MESH':
   me=o.data;coords=np.array([v.co[:] for v in me.vertices],dtype=np.float32);groups=[g.name for g in o.vertex_groups]
   result[o.name]={'coords':hashlib.sha256(coords.tobytes()).hexdigest(),'faces':hashlib.sha256(str([p.vertices[:] for p in me.polygons]).encode()).hexdigest(),'uv':[(u.name,hashlib.sha256(np.array([d.uv[:] for d in u.data],dtype=np.float32).tobytes()).hexdigest()) for u in me.uv_layers],'materials':[x.name for x in me.materials],'modifiers':[(q.name,q.type,q.show_viewport,q.show_render,getattr(q,'levels',None),getattr(q,'render_levels',None)) for q in o.modifiers],'weights':[{groups[g.group]:g.weight for g in v.groups} for v in me.vertices]}
 return result
before=capture();bones={b.name:(tuple(b.head_local),tuple(b.tail_local),b.parent.name if b.parent else None) for b in bpy.data.objects['Armature'].data.bones}
bpy.ops.wm.open_mainfile(filepath=str(P/'zombi1_hair_rigged.blend'));after=capture()
for name in before:
 for field in ('coords','faces','uv','materials','modifiers'):assert before[name][field]==after[name][field],(name,field)
assert before['remesh.001']==after['remesh.001']
a=bpy.data.objects['Armature']
for n,v in bones.items():
 b=a.data.bones[n];assert v==(tuple(b.head_local),tuple(b.tail_local),b.parent.name if b.parent else None),n
changed=0;nonhair_unchanged=0
for w0,w1 in zip(before['remesh']['weights'],after['remesh']['weights']):
 hair=sum(v for n,v in w1.items() if n.startswith('Hair'))
 if hair>1e-8:changed+=1;assert abs(sum(w1.values())-1)<1e-5
 else:assert w0==w1;nonhair_unchanged+=1
report={'existing_bones_unchanged':len(bones),'geometry_uv_materials_modifiers_unchanged':True,'duplicate_model_unchanged':True,'weighted_vertices':changed,'untouched_vertex_weights':nonhair_unchanged,'all_hair_bones_have_influence':{b.name:sum(w.get(b.name,0) for w in after['remesh']['weights']) for b in a.data.bones if b.name.startswith('Hair')}}
assert all(v>0 for v in report['all_hair_bones_have_influence'].values())
P.joinpath('hair_preservation_check.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
