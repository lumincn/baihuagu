# -*- coding: utf-8 -*-
import json, urllib.request, http.cookiejar, re

cj = http.cookiejar.CookieJar()
op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))

def req(url, method='GET', body=None, timeout=30):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method=method)
    if body is not None:
        r.add_header('Content-Type', 'application/json')
    try:
        resp = op.open(r, timeout=timeout)
        return resp.status, resp.read().decode('utf-8', 'ignore')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'ignore')

code, body = req('http://127.0.0.1:5177/api/auth/cli-token', 'POST')
token = json.loads(body)['token']
req(f'http://127.0.0.1:5177/?cli-token={token}')

code, html = req('http://127.0.0.1:5177/local-models')
print('status:', code, 'len:', len(html))
open(r'C:\Users\lumin\src\baihuagu\k8s-test-data\local-models.html', 'w', encoding='utf-8').write(html)
for kw in ['OpenVINO', 'openvino', 'qwen', 'Qwen', 'GPU', 'NPU', '模型', 'bh-openvino', 'CPU', '本地', 'device']:
    print(f'{kw:15s} -> {html.count(kw)}')
m = re.search(r'<title>(.*?)</title>', html)
print('title:', m.group(1) if m else None)
text = re.sub(r'<[^>]+>', ' ', html)
text = re.sub(r'\s+', ' ', text)
print('visible sample:', text[:800])
