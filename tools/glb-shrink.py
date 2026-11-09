#!/usr/bin/env python3
# Shrinks the embedded textures of a .glb so it suits WebGL: the base colour (first image) to <= MAX px,
# every other image to <= OTHER px, all re-encoded as JPEG. Meshes, skins and animations are untouched.
#   python3 tools/glb-shrink.py in.glb out.glb [MAX=1024] [OTHER=256]      (needs Pillow: pip install pillow)
import io, json, struct, sys
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
MAX = int(sys.argv[3]) if len(sys.argv) > 3 else 1024
OTHER = int(sys.argv[4]) if len(sys.argv) > 4 else 256
d = open(src, 'rb').read()
jlen = struct.unpack('<I', d[12:16])[0]
j = json.loads(d[20:20 + jlen])
boff = 20 + jlen
blen = struct.unpack('<I', d[boff:boff + 4])[0]
bin_ = d[boff + 8:boff + 8 + blen]
views = j['bufferViews']
image_views = {img['bufferView']: i for i, img in enumerate(j.get('images', [])) if 'bufferView' in img}
out = bytearray()
for vi, v in enumerate(views):
    chunk = bin_[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
    if vi in image_views:
        im = Image.open(io.BytesIO(chunk)).convert('RGB')
        m = MAX if image_views[vi] == 0 else OTHER
        im.thumbnail((m, m), Image.LANCZOS)
        buf = io.BytesIO(); im.save(buf, 'JPEG', quality=88); chunk = buf.getvalue()
        j['images'][image_views[vi]]['mimeType'] = 'image/jpeg'
    while len(out) % 4: out += b'\0'
    v['byteOffset'] = len(out); v['byteLength'] = len(chunk); v.pop('byteStride', None) if vi in image_views else None
    out += chunk
while len(out) % 4: out += b'\0'
j['buffers'][0]['byteLength'] = len(out)
js = json.dumps(j, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
total = 12 + 8 + len(js) + 8 + len(out)
with open(dst, 'wb') as f:
    f.write(struct.pack('<4sII', b'glTF', 2, total))
    f.write(struct.pack('<I4s', len(js), b'JSON')); f.write(js)
    f.write(struct.pack('<I4s', len(out), b'BIN\0')); f.write(out)
print('wrote', dst, total, 'bytes')
