exec(open(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work\visual_inspect.py',encoding='utf-8-sig').read().split('stats={}')[0])
print('MATRICES',m.matrix_world[:],a.matrix_world[:]);print('ANIM',a.animation_data, 'SHAPE',m.data.shape_keys)
a.data.pose_position='REST';bpy.context.view_layer.update()
s=bpy.context.scene;s.render.engine='BLENDER_WORKBENCH';s.render.resolution_x=800;s.render.resolution_y=800;s.render.resolution_percentage=100;s.display.shading.light='STUDIO';s.display.shading.color_type='SINGLE';s.display.shading.single_color=(0.65,0.65,0.65);s.display.shading.show_cavity=True
cam=bpy.data.objects['Camera'];s.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.2
s.render.image_settings.file_format='JPEG'
for name,pos in [('rest_front',(0,-3,0.5)),('rest_side',(3,0,0.5))]:
 cam.location=pos;cam.rotation_euler=(Vector((0.025,0,0.5))-cam.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(p/(name+'.jpg'));bpy.ops.render.render(write_still=True)
