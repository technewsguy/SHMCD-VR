import sys, UnityPy
env = UnityPy.load(sys.argv[1])
for obj in env.objects:
    if obj.type.name in ("BuildSettings", "PlayerSettings"):
        tree = obj.read_typetree()
        def walk(d, p=""):
            for k, v in d.items():
                kp = p + "." + k
                if any(s in k.lower() for s in ("vr", "xr", "stereo", "enabledvr")):
                    print(obj.type.name, kp, "=", v if not isinstance(v, (bytes,)) else "<bytes>")
                if isinstance(v, dict): walk(v, kp)
        walk(tree)
