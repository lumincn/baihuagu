# -*- coding: utf-8 -*-
import sqlite3, os, glob, sys

d = r'C:\Users\lumin\src\baihuagu\k8s-test-data\db-dump'
for f in sorted(glob.glob(os.path.join(d, '*.db'))):
    try:
        con = sqlite3.connect(f)
        tables = [r[0] for r in con.execute("select name from sqlite_master where type='table' order by name")]
        has_hist = con.execute("select name from sqlite_master where type='table' and name='__EFMigrationsHistory'").fetchone()
        migs = [r[0] for r in con.execute("select MigrationId from __EFMigrationsHistory order by MigrationId")] if has_hist else []
        print(os.path.basename(f), '| tables:', len(tables), '| migrations:', len(migs))
        print('   tables:', tables)
        if migs:
            print('   migs:', [m.split('_')[-1][:40] for m in migs][:10])
        con.close()
    except Exception as e:
        print(os.path.basename(f), 'ERR', e)
