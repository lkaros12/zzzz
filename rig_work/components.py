import bpy,json,collections
m=bpy.data.objects['remesh'];a=bpy.data.objects['Armature']
print('MOD',[(q.name,q.show_viewport,q.show_render,q.use_deform_preserve_volume) for q in m.modifiers]);print('ACTION',a.animation_data.action if a.animation_data else None);print('FRAME',bpy.context.scene.frame_current)
p=list(range(len(m.data.vertices)))
def root(i):
 while p[i]!=i:p[i]=p[p[i]];i=p[i]
 return i
for e in m.data.edges:
 x,y=map(root,e.vertices);p[x]=y
cs=collections.defaultdict(list)
for v in m.data.vertices:cs[root(v.index)].append(v)
out=[]
for vs in sorted(cs.values(),key=len,reverse=True):
 if len(vs)<30:continue
 weights=collections.Counter()
 for v in vs:
  for g in v.groups:weights[m.vertex_groups[g.group].name]+=g.weight
 out.append({'n':len(vs),'min':[min(v.co[i] for v in vs) for i in range(3)],'max':[max(v.co[i] for v in vs) for i in range(3)],'groups':weights.most_common(4)})
print('COMPONENTS',len(cs),json.dumps(out))
