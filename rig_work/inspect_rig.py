import bpy,json
from pathlib import Path
out={'version':bpy.app.version_string,'objects':[]}
for o in bpy.data.objects:
 d={'name':o.name,'type':o.type,'parent':o.parent.name if o.parent else None,'location':list(o.location),'rotation':list(o.rotation_euler),'scale':list(o.scale)}
 if o.type=='ARMATURE':
  d['bones']=[{'name':b.name,'parent':b.parent.name if b.parent else None,'head':list(b.head_local),'tail':list(b.tail_local),'deform':b.use_deform,'connected':b.use_connect} for b in o.data.bones]
  d['constraints']=[{'bone':p.name,'constraints':[c.type for c in p.constraints]} for p in o.pose.bones if p.constraints]
 if o.type=='MESH':
  d['vertices']=len(o.data.vertices);d['groups']=[g.name for g in o.vertex_groups]
  d['modifiers']=[{'type':m.type,'target':m.object.name if m.type=='ARMATURE' and m.object else None} for m in o.modifiers]
  d['bounds']=[list(v) for v in o.bound_box]
  d['unweighted']=sum(not any(g.weight>0 for g in v.groups) for v in o.data.vertices)
 out['objects'].append(d)
Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work\inspection.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
print(json.dumps(out))
