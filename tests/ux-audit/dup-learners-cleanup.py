"""
重复成员「小明 ×2」清理脚本（审计 P1 数据问题）
- dry-run（默认）：只报告，不修改
- 执行：python dup-learners-cleanup.py --apply
执行前自动备份 family.db 到同目录 backups/
"""
import sqlite3, shutil, sys, os, datetime

DB = r'C:\Users\lumin\.baihua\db\family.db'
APPLY = '--apply' in sys.argv

db = sqlite3.connect(DB)
cur = db.cursor()

# 1. 找出重名学习者
dups = cur.execute("""
    SELECT Name, GROUP_CONCAT(Id) ids, COUNT(*) c
    FROM LearnerProfiles GROUP BY Name HAVING c > 1
""").fetchall()

if not dups:
    print("✅ 无重复成员，无需清理")
    sys.exit(0)

print("发现的重复成员：")
for name, ids, c in dups:
    print(f"  「{name}」×{c}，Id = {ids}")

# 2. 对每组：保留 Id 最小且有学习记录的，其余检查引用
for name, ids, c in dups:
    id_list = [int(x) for x in ids.split(',')]
    for lid in id_list:
        refs = 0
        tables = ['StudyActivities', 'Achievements', 'CheckinMakeupRecords', 'CardReviewStates',
                  'ApprenticeProfiles', 'Masters', 'MasterConversations', 'StageSummaries',
                  'VaultFocusStates', 'VaultFreeStates', 'FamilyRewards', 'RewardClaims',
                  'ChatMemoryEntries']
        for t in tables:
            try:
                cols = [col[1] for col in cur.execute(f"PRAGMA table_info({t})").fetchall()]
                for col in [c for c in cols if c.lower().startswith('learner')]:
                    refs += cur.execute(f"SELECT COUNT(*) FROM {t} WHERE {col}=?", (lid,)).fetchone()[0]
            except Exception:
                pass
        print(f"  LearnerId={lid}: 引用数={refs}")

    if not APPLY:
        print("\n[dry-run] 未做修改。确认后执行：python dup-learners-cleanup.py --apply")
        sys.exit(0)

    # 3. 备份
    os.makedirs(os.path.join(os.path.dirname(DB), 'backups'), exist_ok=True)
    bak = os.path.join(os.path.dirname(DB), 'backups',
                       f'family-{datetime.datetime.now():%Y%m%d-%H%M%S}-before-dup-cleanup.db')
    shutil.copy2(DB, bak)
    print(f"备份已写入: {bak}")

    # 4. 删除有引用之外的多余学习者（保留有学习记录的；都无引用则保留 Id 最小）
    keep = min(id_list)
    for lid in id_list:
        if lid == keep:
            continue
        cur.execute("DELETE FROM LearnerProfiles WHERE Id=?", (lid,))
        print(f"已删除 LearnerProfiles Id={lid}")
    db.commit()

    # 5. 验证
    left = cur.execute("SELECT Id, Name FROM LearnerProfiles").fetchall()
    print("清理后成员：", left)
    dups_left = cur.execute(
        "SELECT COUNT(*) FROM (SELECT Name FROM LearnerProfiles GROUP BY Name HAVING COUNT(*)>1)").fetchone()[0]
    print("剩余重名组：", dups_left)
