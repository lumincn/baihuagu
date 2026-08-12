#!/bin/bash
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
for d in bh-family bh-ai bh-vault bh-webui bh-nginx; do
  echo "=== $d ==="
  kubectl get deployment $d -n baihua -o jsonpath='hostNetwork={.spec.template.spec.hostNetwork} ports={.spec.template.spec.containers[0].ports}'
  echo
done
echo "=== svc ==="
kubectl get svc -n baihua -o wide
echo "=== k3s 状态 ==="
systemctl is-active k3s
ps aux | grep k3s-server | grep -v grep | awk '{print "pid="$2, "etime="$10}'
