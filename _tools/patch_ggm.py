# Usage: patch_ggm.py <globalgamemanagers> Device1 [Device2...]  -- sets BuildSettings.enabledVRDevices
import sys, UnityPy, shutil, os
path, devices = sys.argv[1], sys.argv[2:]
if not os.path.exists(path + ".orig"): shutil.copy2(path, path + ".orig")
env = UnityPy.load(path + ".orig")
for obj in env.objects:
    if obj.type.name == "BuildSettings":
        t = obj.read_typetree(); t["enabledVRDevices"] = devices; obj.save_typetree(t)
data = env.file.save()
open(path, "wb").write(data)
print("enabledVRDevices =", devices, "size", os.path.getsize(path + ".orig"), "->", len(data))
