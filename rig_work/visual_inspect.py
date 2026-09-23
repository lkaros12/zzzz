import bpy,json
from mathutils import Vector
from pathlib import Path
p=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work')
m=bpy.data.objects['remesh']; a=bpy.data.objects['Armature']
stats={}
for g in m.vertex_groups:
 pts=[(v.co,w.weight) for v in m.data.vertices for w in v.groups if w.group==g.index and w.weight>0.05]
 if pts:
  total=sum(w for v,w in pts); c=sum((v*w for v,w in pts),Vector())/total
  stats[g.name]={'n':len(pts),'center':list(c),'min':[min(v[i] for v,w in pts) for i in range(3)],'max':[max(v[i] for v,w in pts) for i in range(3)]}
print('STATS',json.dumps(stats))
print('POSE',[(b.name,list(b.rotation_euler),list(b.rotation_quaternion),list(b.scale)) for b in a.pose.bones if b.matrix_basis!=__import__('mathutils').Matrix.Identity(4)])
p.joinpath('weight_stats.json').write_text(json.dumps(stats,indent=2))
s=bpy.context.scene;s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=900;s.render.resolution_y=900;s.render.resolution_percentage=100
s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(0.65,0.65,0.65);s.display.shading.show_shadows=True;s.display.shading.show_cavity=True
cam=s.camera or bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.2
for name,pos in [('front',(0,-3,0.5)),('side',(3,0,0.5))]:
 cam.location=pos;cam.rotation_euler=(Vector((0,0,0.5))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(p/(name+'.png'));bpy.ops.render.render(write_still=True)
