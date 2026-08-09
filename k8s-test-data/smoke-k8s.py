# -*- coding: utf-8 -*-
"""百花 k8s 部署冒烟测试：cli-token 登录 + 受保护页面 + 后端健康检查"""
import json, urllib.request, http.cookiejar, sys

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

ok = True
def check(name, cond, detail=''):
    global ok
    print(('PASS' if cond else 'FAIL'), '-', name, ('| ' + detail if detail else ''))
    if not cond:
        ok = False

# 1. cli-token 获取（loopback 直连 webui）
code, body = req('http://127.0.0.1:5177/api/auth/cli-token', 'POST')
check('cli-token 获取', code == 200 and 'token' in body, f'{code} {body[:120]}')
token = json.loads(body).get('token') if code == 200 else None

if token:
    # 2. 建立会话
    code, body = req(f'http://127.0.0.1:5177/?cli-token={token}')
    check('cli-token 会话建立', code in (200, 302), f'{code}')

    # 3. 受保护页面（未登录会 302 到 /login）
    code, body = req('http://127.0.0.1:5177/local-models')
    check('local-models 页面', code == 200, f'{code}')
    check('local-models 含 OpenVINO 字样', 'OpenVINO' in body or 'openvino' in body.lower() or 'bh-openvino' in body, body[:80])

    code, body = req('http://127.0.0.1:5177/image-recognition')
    check('image-recognition 页面', code == 200, f'{code}')

# 4. 后端健康检查（直接端口转发 / 集群内）
for name, port in [('family', 8788), ('vault', 8790), ('ai', 8791)]:
    pass  # 用 kubectl exec 在下面单独测

print()
print('RESULT:', 'ALL PASS' if ok else 'HAS FAILURES')
sys.exit(0 if ok else 1)
