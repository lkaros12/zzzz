from pathlib import Path
p=Path(r'C:\Users\MBC-501-03\Desktop\zzzz\rig_work')
t=p.joinpath('pose_tests.py').read_text(encoding='utf-8-sig')
t=t.replace('base=np.array',"cases={'toes':[('LeftToeBase',(1,0,0),-25),('RightToeBase',(1,0,0),-25)],'wrists':[('LeftHand',(0,0,1),-30),('RightHand',(0,0,1),30)]}\nbase=np.array")
t=t.replace("'pose_tests.json'","'extremity_tests.json'")
exec(t)
