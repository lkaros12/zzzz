import bpy,json,numpy as np
from pathlib import Path
from mathutils import Vector
P=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work');m=bpy.data.objects['remesh'];s=bpy.context.scene
s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=800;s.render.resolution_y=900;s.render.resolution_percentage=100;s.render.image_settings.file_format='JPEG';s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(.65,.65,.65);s.display.shading.show_cavity=True
cam=bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.85
for name,pos in [('hair_front',(.025,-3,.71)),('hair_back',(.025,3,.71)),('hair_side',(3,0,.71))]:
 cam.location=pos;cam.rotation_euler=(Vector((.025,0,.68))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(P/(name+'.jpg'));bpy.ops.render.render(write_still=True)
