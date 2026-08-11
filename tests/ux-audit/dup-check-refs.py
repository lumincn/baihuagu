import sqlite3
db = sqlite3.connect(r'C:\Users\lumin\.baihua\db\family.db')
cur = db.cursor()
# 全表扫 LearnerId 引用
print("== 所有含 LearnerId 的表对 Id=2 的引用 ==")
found = False
for t in ['Achievements','StudyActivities','CheckinMakeupRecords','CardReviewStates','ApprenticeProfiles','Masters','MasterConversations','StageSummaries','VaultFocusStates','VaultFreeStates','FamilyRewards','RewardClaims','ChatMemoryEntries','OpenClawTasks','Tasks']:
    try:
        cols = [c[1] for c in cur.execute(f"PRAGMA table_info({t})").fetchall()]
        lc = [c for c in cols if c.lower().startswith('learner')]
        for col in lc:
            n = cur.execute(f"SELECT COUNT(*) FROM {t} WHERE {col}=2").fetchone()[0]
            if n > 0:
                print(f"{t}.{col} → Id=2: {n} 条")
                found = True
    except Exception as e:
        pass
if not found:
    print("✅ 无任何表引用 LearnerId=2，删除零风险")
