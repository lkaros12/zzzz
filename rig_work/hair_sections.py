import bpy,numpy as np
v=np.array([v.co[:] for v in bpy.data.objects['remesh'].data.vertices]);c=.025518365
for z in [.59,.63,.67,.71,.75,.79,.83]:
 for x in [0,.06,.12,.18]:
  q=v[(abs(v[:,0]-c-x)<.015)&(abs(v[:,2]-z)<.01)]
  if len(q):print(z,x,len(q),np.quantile(q[:,1],[0,.1,.25,.5,.75,1]).round(4).tolist())
