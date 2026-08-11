import sqlite3, json
db = sqlite3.connect(r'C:\Users\lumin\.baihua\db\family.db')
cur = db.cursor()
# 表清单
tables = [r[0] for r in cur.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()]
print("表:", tables)
# 学习者
print("\n== LearnerProfiles ==")
for r in cur.execute("SELECT Id, Name, AvatarEmoji, Color, IsDefault FROM LearnerProfiles").fetchall():
    print(r)
# 重名检测
print("\n== 重名 ==")
for r in cur.execute("SELECT Name, COUNT(*) c, GROUP_CONCAT(Id) ids FROM LearnerProfiles GROUP BY Name HAVING c > 1").fetchall():
    print(r)
# 各 Learner 的引用分布
print("\n== 引用分布 ==")
for t in ['StudyActivities','Achievements','CheckinMakeupRecords','QuizSessions','QuizAnswers','DailyCardRecords']:
    try:
        cols = [c[1] for c in cur.execute(f"PRAGMA table_info({t})").fetchall()]
        lc = [c for c in cols if 'earner' in c]
        if lc:
            for r in cur.execute(f"SELECT {lc[0]}, COUNT(*) FROM {t} GROUP BY {lc[0]}").fetchall():
                print(f"{t}.{lc[0]} = {r[0]}: {r[1]} 条")
    except Exception as e:
        print(f"{t}: {e}")
