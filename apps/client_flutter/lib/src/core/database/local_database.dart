import 'dart:io';

import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart' as mobile;
import 'package:sqflite_common/sqlite_api.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

class LocalDatabase {
  LocalDatabase._();

  static const _databaseName = 'erp_accounting_client.db';
  static const _databaseVersion = 1;

  static final LocalDatabase instance = LocalDatabase._();

  Database? _database;
  String? _databasePath;

  bool get isOpen => _database != null;
  String? get databasePath => _databasePath;

  Future<void> initialize() async {
    if (_database != null) return;

    final directory = await getApplicationSupportDirectory();
    final databaseDirectory = Directory(
      p.join(directory.path, 'ERP_Accounting', 'data'),
    );

    await databaseDirectory.create(recursive: true);

    final dbPath = p.join(databaseDirectory.path, _databaseName);
    _databasePath = dbPath;

    final DatabaseFactory factory;

    if (Platform.isWindows) {
      sqfliteFfiInit();
      factory = databaseFactoryFfi;
    } else {
      factory = mobile.databaseFactory;
    }

    _database = await factory.openDatabase(
      dbPath,
      options: OpenDatabaseOptions(
        version: _databaseVersion,
        onConfigure: (db) async {
          await db.execute('PRAGMA foreign_keys = ON');
        },
        onCreate: _createSchema,
      ),
    );
  }

  Future<void> _createSchema(Database db, int version) async {
    await db.execute('''
      CREATE TABLE local_meta (
        key TEXT PRIMARY KEY,
        value TEXT,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE TABLE cached_user_profile (
        user_id TEXT PRIMARY KEY,
        company_id TEXT NOT NULL,
        display_name TEXT NOT NULL,
        role TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE TABLE cached_accounts (
        id TEXT PRIMARY KEY,
        company_id TEXT NOT NULL,
        code TEXT NOT NULL,
        name TEXT NOT NULL,
        type TEXT NOT NULL,
        parent_id TEXT,
        is_active INTEGER NOT NULL DEFAULT 1,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE INDEX idx_cached_accounts_company_code
      ON cached_accounts(company_id, code)
    ''');

    await db.execute('''
      CREATE TABLE sync_outbox (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        change_id TEXT NOT NULL UNIQUE,
        company_id TEXT NOT NULL,
        entity_type TEXT NOT NULL,
        entity_id TEXT NOT NULL,
        operation TEXT NOT NULL,
        payload_json TEXT NOT NULL,
        created_at TEXT NOT NULL,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        last_error TEXT,
        sent_at TEXT
      )
    ''');

    await db.execute('''
      CREATE INDEX idx_sync_outbox_pending
      ON sync_outbox(sent_at, created_at)
    ''');
  }

  Database get _db {
    final db = _database;
    if (db == null) {
      throw StateError('LocalDatabase.initialize() must be called first.');
    }
    return db;
  }

  Future<void> cacheUserProfile({
    required String userId,
    required String companyId,
    required String displayName,
    required String role,
  }) async {
    await _db.insert(
      'cached_user_profile',
      {
        'user_id': userId,
        'company_id': companyId,
        'display_name': displayName,
        'role': role,
        'updated_at': DateTime.now().toUtc().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> replaceAccounts({
    required String companyId,
    required List<Map<String, dynamic>> accounts,
  }) async {
    await _db.transaction((txn) async {
      await txn.delete(
        'cached_accounts',
        where: 'company_id = ?',
        whereArgs: [companyId],
      );

      final now = DateTime.now().toUtc().toIso8601String();

      for (final account in accounts) {
        await txn.insert(
          'cached_accounts',
          {
            'id': account['id'] as String,
            'company_id': companyId,
            'code': account['code'] as String,
            'name': account['name'] as String,
            'type': account['type'] as String,
            'parent_id': account['parentId'] as String?,
            'is_active': (account['isActive'] as bool? ?? true) ? 1 : 0,
            'updated_at': now,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
    });
  }

  Future<int> cachedAccountCount(String companyId) async {
    final rows = await _db.rawQuery(
      'SELECT COUNT(*) AS count FROM cached_accounts WHERE company_id = ?',
      [companyId],
    );

    return (rows.first['count'] as int?) ?? 0;
  }

  Future<int> pendingOutboxCount() async {
    final rows = await _db.rawQuery(
      'SELECT COUNT(*) AS count FROM sync_outbox WHERE sent_at IS NULL',
    );

    return (rows.first['count'] as int?) ?? 0;
  }

  Future<void> setMeta(String key, String value) async {
    await _db.insert(
      'local_meta',
      {
        'key': key,
        'value': value,
        'updated_at': DateTime.now().toUtc().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<String?> getMeta(String key) async {
    final rows = await _db.query(
      'local_meta',
      columns: ['value'],
      where: 'key = ?',
      whereArgs: [key],
      limit: 1,
    );

    if (rows.isEmpty) return null;
    return rows.first['value'] as String?;
  }
}
