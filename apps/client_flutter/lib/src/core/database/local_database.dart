import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart' as mobile;
import 'package:sqflite_common/sqlite_api.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';

import '../sync/sync_models.dart';

class LocalDatabase {
  LocalDatabase._();

  static const _databaseName = 'erp_accounting_client.db';
  static const _databaseVersion = 5;

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
        onUpgrade: _upgradeSchema,
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
        account_level TEXT NOT NULL DEFAULT 'Group',
        level_title TEXT NOT NULL DEFAULT '',
        nature TEXT NOT NULL DEFAULT 'Debit',
        nature_title TEXT NOT NULL DEFAULT '',
        is_postable INTEGER NOT NULL DEFAULT 1,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE INDEX idx_cached_accounts_company_code
      ON cached_accounts(company_id, code)
    ''');

    await _createMasterDataSchema(db);

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

    await _createAccountingSchema(db);
  }

  Future<void> _upgradeSchema(
    Database db,
    int oldVersion,
    int newVersion,
  ) async {
    if (oldVersion < 2) {
      await _createAccountingSchema(db);
    }

    if (oldVersion < 3) {
      await db.execute(
        "ALTER TABLE cached_accounts ADD COLUMN account_level TEXT NOT NULL DEFAULT 'Group'",
      );
      await db.execute(
        "ALTER TABLE cached_accounts ADD COLUMN level_title TEXT NOT NULL DEFAULT ''",
      );
      await db.execute(
        "ALTER TABLE cached_accounts ADD COLUMN nature TEXT NOT NULL DEFAULT 'Debit'",
      );
      await db.execute(
        "ALTER TABLE cached_accounts ADD COLUMN nature_title TEXT NOT NULL DEFAULT ''",
      );
      await db.execute(
        "ALTER TABLE cached_accounts ADD COLUMN is_postable INTEGER NOT NULL DEFAULT 1",
      );
    }

    if (oldVersion < 4) {
      await _createMasterDataSchema(db);

      // Versions below 2 create the accounting tables above using the
      // current schema, so their v4 columns already exist.
      if (oldVersion >= 2) {
        await db.execute(
          "ALTER TABLE local_accounting_documents ADD COLUMN fiscal_year_id TEXT",
        );
        await db.execute(
          "ALTER TABLE local_document_lines ADD COLUMN detail_account_id TEXT",
        );
      }
    }

    if (oldVersion < 5) {
      if (oldVersion >= 4) {
        await db.execute(
          "ALTER TABLE cached_detail_accounts ADD COLUMN revision INTEGER NOT NULL DEFAULT 1",
        );
        await db.execute(
          "ALTER TABLE cached_detail_accounts ADD COLUMN sync_status TEXT NOT NULL DEFAULT 'Synced'",
        );
        await db.execute(
          "ALTER TABLE cached_detail_accounts ADD COLUMN sync_error TEXT",
        );
      }

      await _createMasterDataSchema(db);
    }
  }

  Future<void> _createMasterDataSchema(Database db) async {
    await db.execute('''
      CREATE TABLE IF NOT EXISTS cached_fiscal_years (
        id TEXT PRIMARY KEY,
        company_id TEXT NOT NULL,
        name TEXT NOT NULL,
        persian_year INTEGER NOT NULL,
        start_date TEXT NOT NULL,
        end_date TEXT NOT NULL,
        is_default INTEGER NOT NULL DEFAULT 0,
        is_closed INTEGER NOT NULL DEFAULT 0,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_cached_fiscal_years_company
      ON cached_fiscal_years(company_id, start_date, end_date)
    ''');

    await db.execute('''
      CREATE TABLE IF NOT EXISTS cached_detail_accounts (
        id TEXT PRIMARY KEY,
        company_id TEXT NOT NULL,
        code TEXT NOT NULL,
        name TEXT NOT NULL,
        type TEXT NOT NULL,
        national_id TEXT,
        is_active INTEGER NOT NULL DEFAULT 1,
        revision INTEGER NOT NULL DEFAULT 1,
        sync_status TEXT NOT NULL DEFAULT 'Synced',
        sync_error TEXT,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_cached_detail_accounts_company_code
      ON cached_detail_accounts(company_id, code)
    ''');

    await db.execute('''
      CREATE TABLE IF NOT EXISTS sync_conflicts (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        conflict_key TEXT NOT NULL UNIQUE,
        company_id TEXT NOT NULL,
        entity_type TEXT NOT NULL,
        entity_id TEXT NOT NULL,
        local_payload_json TEXT NOT NULL,
        server_payload_json TEXT,
        base_revision INTEGER NOT NULL,
        server_revision INTEGER,
        created_at TEXT NOT NULL,
        resolved_at TEXT,
        resolution TEXT
      )
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_sync_conflicts_unresolved
      ON sync_conflicts(company_id, entity_type, resolved_at, created_at)
    ''');
  }

  Future<void> _createAccountingSchema(Database db) async {
    await db.execute('''
      CREATE TABLE IF NOT EXISTS local_accounting_documents (
        id TEXT PRIMARY KEY,
        company_id TEXT NOT NULL,
        fiscal_year_id TEXT,
        server_id TEXT,
        server_number TEXT,
        document_date TEXT NOT NULL,
        description TEXT,
        status TEXT NOT NULL,
        sync_status TEXT NOT NULL,
        currency TEXT NOT NULL DEFAULT 'IRR',
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL
      )
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_local_documents_company_date
      ON local_accounting_documents(company_id, document_date)
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_local_documents_sync
      ON local_accounting_documents(company_id, sync_status)
    ''');

    await db.execute('''
      CREATE TABLE IF NOT EXISTS local_document_lines (
        id TEXT PRIMARY KEY,
        document_id TEXT NOT NULL,
        account_id TEXT NOT NULL,
        detail_account_id TEXT,
        description TEXT,
        debit INTEGER NOT NULL DEFAULT 0,
        credit INTEGER NOT NULL DEFAULT 0,
        sort_order INTEGER NOT NULL,
        FOREIGN KEY(document_id)
          REFERENCES local_accounting_documents(id)
          ON DELETE CASCADE
      )
    ''');

    await db.execute('''
      CREATE INDEX IF NOT EXISTS idx_local_document_lines_document
      ON local_document_lines(document_id, sort_order)
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
            'type': account['type'].toString(),
            'parent_id': account['parentId'] as String?,
            'is_active': (account['isActive'] as bool? ?? true) ? 1 : 0,
            'account_level': account['level']?.toString() ?? 'Group',
            'level_title': account['levelTitle']?.toString() ?? '',
            'nature': account['nature']?.toString() ?? 'Debit',
            'nature_title': account['natureTitle']?.toString() ?? '',
            'is_postable': (account['isPostable'] as bool? ?? true) ? 1 : 0,
            'updated_at': now,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
    });
  }

  Future<List<CachedAccount>> getCachedAccounts(String companyId) async {
    final rows = await _db.query(
      'cached_accounts',
      where: 'company_id = ?',
      whereArgs: [companyId],
      orderBy: 'code ASC',
    );

    return rows
        .map(
          (row) => CachedAccount(
            id: row['id'] as String,
            companyId: row['company_id'] as String,
            code: row['code'] as String,
            name: row['name'] as String,
            type: row['type'] as String,
            parentId: row['parent_id'] as String?,
            isActive: (row['is_active'] as int) == 1,
            level: row['account_level'] as String,
            levelTitle: row['level_title'] as String,
            nature: row['nature'] as String,
            natureTitle: row['nature_title'] as String,
            isPostable: (row['is_postable'] as int) == 1,
          ),
        )
        .toList(growable: false);
  }

  Future<void> replaceFiscalYears({
    required String companyId,
    required List<Map<String, dynamic>> fiscalYears,
  }) async {
    await _db.transaction((txn) async {
      await txn.delete(
        'cached_fiscal_years',
        where: 'company_id = ?',
        whereArgs: [companyId],
      );

      final now = DateTime.now().toUtc().toIso8601String();

      for (final fiscalYear in fiscalYears) {
        await txn.insert(
          'cached_fiscal_years',
          {
            'id': fiscalYear['id'] as String,
            'company_id': companyId,
            'name': fiscalYear['name'] as String,
            'persian_year': fiscalYear['persianYear'] as int,
            'start_date': fiscalYear['startDate'] as String,
            'end_date': fiscalYear['endDate'] as String,
            'is_default': (fiscalYear['isDefault'] as bool? ?? false) ? 1 : 0,
            'is_closed': (fiscalYear['isClosed'] as bool? ?? false) ? 1 : 0,
            'updated_at': now,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
    });
  }

  Future<void> backfillLegacyJournalFiscalYears(
    String companyId,
  ) async {
    final fiscalYears = await getCachedFiscalYears(companyId);

    if (fiscalYears.isEmpty) return;

    final documents = await _db.query(
      'local_accounting_documents',
      columns: ['id', 'document_date', 'fiscal_year_id'],
      where: 'company_id = ? AND fiscal_year_id IS NULL',
      whereArgs: [companyId],
    );

    if (documents.isEmpty) return;

    await _db.transaction((txn) async {
      for (final document in documents) {
        final documentId = document['id'] as String;
        final documentDate = document['document_date'] as String;

        CachedFiscalYear? match;

        for (final fiscalYear in fiscalYears) {
          if (documentDate.compareTo(fiscalYear.startDate) >= 0 &&
              documentDate.compareTo(fiscalYear.endDate) <= 0) {
            match = fiscalYear;
            break;
          }
        }

        if (match == null) continue;

        await txn.update(
          'local_accounting_documents',
          {
            'fiscal_year_id': match.id,
            'updated_at': DateTime.now().toUtc().toIso8601String(),
          },
          where: 'id = ?',
          whereArgs: [documentId],
        );

        final outboxRows = await txn.query(
          'sync_outbox',
          columns: ['id', 'payload_json'],
          where: 'entity_id = ? AND sent_at IS NULL',
          whereArgs: [documentId],
        );

        for (final outbox in outboxRows) {
          final payload = Map<String, dynamic>.from(
            jsonDecode(outbox['payload_json'] as String) as Map,
          );

          if (payload['fiscalYearId'] == null) {
            payload['fiscalYearId'] = match.id;

            await txn.update(
              'sync_outbox',
              {
                'payload_json': jsonEncode(payload),
              },
              where: 'id = ?',
              whereArgs: [outbox['id']],
            );
          }
        }
      }
    });
  }

  Future<List<CachedFiscalYear>> getCachedFiscalYears(
    String companyId,
  ) async {
    final rows = await _db.query(
      'cached_fiscal_years',
      where: 'company_id = ?',
      whereArgs: [companyId],
      orderBy: 'start_date DESC',
    );

    return rows
        .map(
          (row) => CachedFiscalYear(
            id: row['id'] as String,
            companyId: row['company_id'] as String,
            name: row['name'] as String,
            persianYear: row['persian_year'] as int,
            startDate: row['start_date'] as String,
            endDate: row['end_date'] as String,
            isDefault: (row['is_default'] as int) == 1,
            isClosed: (row['is_closed'] as int) == 1,
          ),
        )
        .toList(growable: false);
  }

  Future<void> replaceDetailAccounts({
    required String companyId,
    required List<Map<String, dynamic>> details,
  }) async {
    await _db.transaction((txn) async {
      await txn.delete(
        'cached_detail_accounts',
        where: 'company_id = ?',
        whereArgs: [companyId],
      );

      final now = DateTime.now().toUtc().toIso8601String();

      for (final detail in details) {
        await txn.insert(
          'cached_detail_accounts',
          {
            'id': detail['id'] as String,
            'company_id': companyId,
            'code': detail['code'] as String,
            'name': detail['name'] as String,
            'type': detail['type'].toString(),
            'national_id': detail['nationalId'] as String?,
            'is_active': (detail['isActive'] as bool? ?? true) ? 1 : 0,
            'updated_at': now,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
    });
  }

  Future<List<CachedDetailAccount>> getCachedDetailAccounts(
    String companyId,
  ) async {
    final rows = await _db.query(
      'cached_detail_accounts',
      where: 'company_id = ? AND is_active = 1',
      whereArgs: [companyId],
      orderBy: 'code ASC',
    );

    return rows
        .map(
          (row) => CachedDetailAccount(
            id: row['id'] as String,
            companyId: row['company_id'] as String,
            code: row['code'] as String,
            name: row['name'] as String,
            type: row['type'] as String,
            nationalId: row['national_id'] as String?,
            isActive: (row['is_active'] as int) == 1,
          ),
        )
        .toList(growable: false);
  }

  Future<int> cachedAccountCount(String companyId) async {
    final rows = await _db.rawQuery(
      'SELECT COUNT(*) AS count FROM cached_accounts WHERE company_id = ?',
      [companyId],
    );

    return (rows.first['count'] as int?) ?? 0;
  }

  Future<String> saveLocalJournal({
    required String companyId,
    required String? fiscalYearId,
    required DateTime documentDate,
    required String description,
    required List<LocalJournalLineInput> lines,
    required bool queueForSync,
  }) async {
    final effectiveLines = lines
        .where((line) => line.debit > 0 || line.credit > 0)
        .toList(growable: false);

    if (effectiveLines.isEmpty) {
      throw ArgumentError('حداقل یک ردیف دارای مبلغ برای سند لازم است.');
    }

    for (final line in effectiveLines) {
      if (line.debit < 0 || line.credit < 0) {
        throw ArgumentError('مبلغ بدهکار و بستانکار نمی‌تواند منفی باشد.');
      }
      if (line.debit > 0 && line.credit > 0) {
        throw ArgumentError(
          'در هر ردیف فقط یکی از بدهکار یا بستانکار می‌تواند مبلغ داشته باشد.',
        );
      }
    }

    final debitTotal =
        effectiveLines.fold<int>(0, (sum, line) => sum + line.debit);
    final creditTotal =
        effectiveLines.fold<int>(0, (sum, line) => sum + line.credit);

    if (queueForSync) {
      if (fiscalYearId == null || fiscalYearId.isEmpty) {
        throw ArgumentError(
          'سال مالی برای سند آماده همگام‌سازی الزامی است.',
        );
      }

      if (effectiveLines.length < 2) {
        throw ArgumentError('سند آماده همگام‌سازی حداقل دو ردیف نیاز دارد.');
      }
      if (debitTotal <= 0 || debitTotal != creditTotal) {
        throw ArgumentError(
          'برای ثبت، جمع بدهکار و بستانکار باید برابر و بزرگ‌تر از صفر باشد.',
        );
      }
    }

    final documentId = _newId();
    final now = DateTime.now().toUtc().toIso8601String();
    final dateOnly = _dateOnly(documentDate);

    await _db.transaction((txn) async {
      await txn.insert(
        'local_accounting_documents',
        {
          'id': documentId,
          'company_id': companyId,
          'fiscal_year_id': fiscalYearId,
          'document_date': dateOnly,
          'description': description.trim(),
          'status': queueForSync ? 'PendingSync' : 'Draft',
          'sync_status': queueForSync ? 'Pending' : 'LocalOnly',
          'currency': 'IRR',
          'created_at': now,
          'updated_at': now,
        },
      );

      var sortOrder = 0;
      for (final line in effectiveLines) {
        await txn.insert(
          'local_document_lines',
          {
            'id': _newId(),
            'document_id': documentId,
            'account_id': line.accountId,
            'detail_account_id': line.detailAccountId,
            'description': line.description.trim(),
            'debit': line.debit,
            'credit': line.credit,
            'sort_order': sortOrder++,
          },
        );
      }

      if (queueForSync) {
        final payload = <String, dynamic>{
          'localDocumentId': documentId,
          'companyId': companyId,
          'fiscalYearId': fiscalYearId,
          'documentDate': dateOnly,
          'description': description.trim(),
          'currency': 'IRR',
          'debitTotal': debitTotal,
          'creditTotal': creditTotal,
          'lines': effectiveLines
              .map(
                (line) => {
                  'accountId': line.accountId,
                  'detailAccountId': line.detailAccountId,
                  'description': line.description.trim(),
                  'debit': line.debit,
                  'credit': line.credit,
                },
              )
              .toList(growable: false),
        };

        await txn.insert(
          'sync_outbox',
          {
            'change_id': _newId(),
            'company_id': companyId,
            'entity_type': 'AccountingDocument',
            'entity_id': documentId,
            'operation': 'Create',
            'payload_json': jsonEncode(payload),
            'created_at': now,
            'attempt_count': 0,
          },
        );
      }
    });

    return documentId;
  }

  Future<int> getJournalPullCursor(String companyId) async {
    final value = await getMeta('journal_pull_cursor:' + companyId);
    return int.tryParse(value ?? '') ?? 0;
  }

  Future<void> applyServerJournalPage({
    required String companyId,
    required JournalPullPage page,
  }) async {
    final now = DateTime.now().toUtc().toIso8601String();

    await _db.transaction((txn) async {
      for (final change in page.changes) {
        final existingRows = await txn.query(
          'local_accounting_documents',
          columns: ['id', 'created_at'],
          where: 'company_id = ? AND server_id = ?',
          whereArgs: [companyId, change.journalEntryId],
          limit: 1,
        );

        final String localDocumentId;
        final String createdAt;

        if (existingRows.isNotEmpty) {
          localDocumentId = existingRows.first['id'] as String;
          createdAt = existingRows.first['created_at'] as String;
        } else {
          localDocumentId = 'server:' + change.journalEntryId;
          createdAt =
              change.postedAt?.toUtc().toIso8601String() ?? now;
        }

        final values = <String, Object?>{
          'company_id': companyId,
          'fiscal_year_id': change.fiscalYearId,
          'server_id': change.journalEntryId,
          'server_number': change.number,
          'document_date': change.documentDate,
          'description': change.description ?? '',
          'status': change.status,
          'sync_status': 'Synced',
          'currency': 'IRR',
          'created_at': createdAt,
          'updated_at': now,
        };

        if (existingRows.isEmpty) {
          await txn.insert(
            'local_accounting_documents',
            {
              'id': localDocumentId,
              ...values,
            },
          );
        } else {
          await txn.update(
            'local_accounting_documents',
            values,
            where: 'id = ?',
            whereArgs: [localDocumentId],
          );
        }

        await txn.delete(
          'local_document_lines',
          where: 'document_id = ?',
          whereArgs: [localDocumentId],
        );

        var sortOrder = 0;

        for (final line in change.lines) {
          await txn.insert(
            'local_document_lines',
            {
              'id': _newId(),
              'document_id': localDocumentId,
              'account_id': line.accountId,
              'detail_account_id': line.detailAccountId,
              'description': line.description ?? '',
              'debit': line.debit,
              'credit': line.credit,
              'sort_order': sortOrder++,
            },
          );
        }
      }

      await txn.insert(
        'local_meta',
        {
          'key': 'journal_pull_cursor:' + companyId,
          'value': page.nextCursor.toString(),
          'updated_at': now,
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    });
  }

  Future<List<LocalAccountingDocument>> getLocalDocuments(
    String companyId,
  ) async {
    final documents = await _db.query(
      'local_accounting_documents',
      where: 'company_id = ?',
      whereArgs: [companyId],
      orderBy: 'document_date DESC, created_at DESC',
    );

    final result = <LocalAccountingDocument>[];

    for (final document in documents) {
      final totals = await _db.rawQuery(
        '''
        SELECT
          COALESCE(SUM(debit), 0) AS debit_total,
          COALESCE(SUM(credit), 0) AS credit_total,
          COUNT(*) AS line_count
        FROM local_document_lines
        WHERE document_id = ?
        ''',
        [document['id']],
      );

      final summary = totals.first;

      final outboxRows = await _db.query(
        'sync_outbox',
        columns: ['attempt_count', 'last_error'],
        where: 'entity_id = ? AND sent_at IS NULL',
        whereArgs: [document['id']],
        orderBy: 'id DESC',
        limit: 1,
      );

      final pendingOutbox =
          outboxRows.isEmpty ? null : outboxRows.first;

      result.add(
        LocalAccountingDocument(
          id: document['id'] as String,
          companyId: document['company_id'] as String,
          documentDate: document['document_date'] as String,
          description: document['description'] as String? ?? '',
          status: document['status'] as String,
          syncStatus: document['sync_status'] as String,
          debitTotal: (summary['debit_total'] as num).toInt(),
          creditTotal: (summary['credit_total'] as num).toInt(),
          lineCount: (summary['line_count'] as num).toInt(),
          serverNumber: document['server_number'] as String?,
          syncAttempts:
              (pendingOutbox?['attempt_count'] as int?) ?? 0,
          syncError: pendingOutbox?['last_error'] as String?,
        ),
      );
    }

    return result;
  }

  Future<List<LocalJournalLine>> getLocalDocumentLines(
    String documentId,
  ) async {
    final rows = await _db.rawQuery(
      '''
      SELECT
        l.id,
        l.document_id,
        l.account_id,
        l.detail_account_id,
        l.description,
        l.debit,
        l.credit,
        a.code AS account_code,
        a.name AS account_name,
        d.code AS detail_code,
        d.name AS detail_name
      FROM local_document_lines l
      LEFT JOIN cached_accounts a ON a.id = l.account_id
      LEFT JOIN cached_detail_accounts d ON d.id = l.detail_account_id
      WHERE l.document_id = ?
      ORDER BY l.sort_order ASC
      ''',
      [documentId],
    );

    return rows
        .map(
          (row) => LocalJournalLine(
            id: row['id'] as String,
            documentId: row['document_id'] as String,
            accountId: row['account_id'] as String,
            accountCode: row['account_code'] as String? ?? '',
            accountName: row['account_name'] as String? ?? 'حساب نامشخص',
            detailAccountId: row['detail_account_id'] as String?,
            detailCode: row['detail_code'] as String?,
            detailName: row['detail_name'] as String?,
            description: row['description'] as String? ?? '',
            debit: (row['debit'] as num).toInt(),
            credit: (row['credit'] as num).toInt(),
          ),
        )
        .toList(growable: false);
  }

  Future<List<PendingOutboxItem>> getPendingOutbox({
    int limit = 50,
  }) async {
    final rows = await _db.query(
      'sync_outbox',
      where: 'sent_at IS NULL',
      orderBy: 'created_at ASC, id ASC',
      limit: limit,
    );

    return rows
        .map(
          (row) => PendingOutboxItem(
            id: row['id'] as int,
            changeId: row['change_id'] as String,
            companyId: row['company_id'] as String,
            entityType: row['entity_type'] as String,
            entityId: row['entity_id'] as String,
            operation: row['operation'] as String,
            payload: Map<String, dynamic>.from(
              jsonDecode(row['payload_json'] as String) as Map,
            ),
            attemptCount: row['attempt_count'] as int,
            lastError: row['last_error'] as String?,
          ),
        )
        .toList(growable: false);
  }

  Future<void> markOutboxSent({
    required int outboxId,
    required String localDocumentId,
    required String serverId,
    required String serverNumber,
  }) async {
    final now = DateTime.now().toUtc().toIso8601String();

    await _db.transaction((txn) async {
      await txn.update(
        'sync_outbox',
        {
          'sent_at': now,
          'last_error': null,
        },
        where: 'id = ?',
        whereArgs: [outboxId],
      );

      await txn.update(
        'local_accounting_documents',
        {
          'server_id': serverId,
          'server_number': serverNumber,
          'status': 'Posted',
          'sync_status': 'Synced',
          'updated_at': now,
        },
        where: 'id = ?',
        whereArgs: [localDocumentId],
      );
    });
  }

  Future<void> markOutboxFailed({
    required int outboxId,
    required String error,
  }) async {
    await _db.rawUpdate(
      '''
      UPDATE sync_outbox
      SET attempt_count = attempt_count + 1,
          last_error = ?
      WHERE id = ?
      ''',
      [error, outboxId],
    );
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

  static String _dateOnly(DateTime value) {
    final year = value.year.toString().padLeft(4, '0');
    final month = value.month.toString().padLeft(2, '0');
    final day = value.day.toString().padLeft(2, '0');
    return '$year-$month-$day';
  }

  static String _newId() {
    final random = Random.secure();
    final bytes = List<int>.generate(16, (_) => random.nextInt(256));

    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;

    String hex(int value) => value.toRadixString(16).padLeft(2, '0');

    final value = bytes.map(hex).join();
    return value.substring(0, 8) +
        '-' +
        value.substring(8, 12) +
        '-' +
        value.substring(12, 16) +
        '-' +
        value.substring(16, 20) +
        '-' +
        value.substring(20);
  }
}

class CachedAccount {
  const CachedAccount({
    required this.id,
    required this.companyId,
    required this.code,
    required this.name,
    required this.type,
    required this.parentId,
    required this.isActive,
    required this.level,
    required this.levelTitle,
    required this.nature,
    required this.natureTitle,
    required this.isPostable,
  });

  final String id;
  final String companyId;
  final String code;
  final String name;
  final String type;
  final String? parentId;
  final bool isActive;
  final String level;
  final String levelTitle;
  final String nature;
  final String natureTitle;
  final bool isPostable;
}

class LocalJournalLineInput {
  const LocalJournalLineInput({
    required this.accountId,
    required this.description,
    required this.debit,
    required this.credit,
    this.detailAccountId,
  });

  final String accountId;
  final String description;
  final int debit;
  final int credit;
  final String? detailAccountId;
}

class LocalAccountingDocument {
  const LocalAccountingDocument({
    required this.id,
    required this.companyId,
    required this.documentDate,
    required this.description,
    required this.status,
    required this.syncStatus,
    required this.debitTotal,
    required this.creditTotal,
    required this.lineCount,
    required this.serverNumber,
    required this.syncAttempts,
    required this.syncError,
  });

  final String id;
  final String companyId;
  final String documentDate;
  final String description;
  final String status;
  final String syncStatus;
  final int debitTotal;
  final int creditTotal;
  final int lineCount;
  final String? serverNumber;
  final int syncAttempts;
  final String? syncError;

  bool get isBalanced => debitTotal == creditTotal && debitTotal > 0;
}

class LocalJournalLine {
  const LocalJournalLine({
    required this.id,
    required this.documentId,
    required this.accountId,
    required this.accountCode,
    required this.accountName,
    required this.detailAccountId,
    required this.detailCode,
    required this.detailName,
    required this.description,
    required this.debit,
    required this.credit,
  });

  final String id;
  final String documentId;
  final String accountId;
  final String accountCode;
  final String accountName;
  final String? detailAccountId;
  final String? detailCode;
  final String? detailName;
  final String description;
  final int debit;
  final int credit;
}


class CachedFiscalYear {
  const CachedFiscalYear({
    required this.id,
    required this.companyId,
    required this.name,
    required this.persianYear,
    required this.startDate,
    required this.endDate,
    required this.isDefault,
    required this.isClosed,
  });

  final String id;
  final String companyId;
  final String name;
  final int persianYear;
  final String startDate;
  final String endDate;
  final bool isDefault;
  final bool isClosed;

  bool contains(DateTime date) {
    final value = LocalDatabase._dateOnly(date);
    return value.compareTo(startDate) >= 0 &&
        value.compareTo(endDate) <= 0;
  }
}

class CachedDetailAccount {
  const CachedDetailAccount({
    required this.id,
    required this.companyId,
    required this.code,
    required this.name,
    required this.type,
    required this.nationalId,
    required this.isActive,
  });

  final String id;
  final String companyId;
  final String code;
  final String name;
  final String type;
  final String? nationalId;
  final bool isActive;
}


class PendingOutboxItem {
  const PendingOutboxItem({
    required this.id,
    required this.changeId,
    required this.companyId,
    required this.entityType,
    required this.entityId,
    required this.operation,
    required this.payload,
    required this.attemptCount,
    required this.lastError,
  });

  final int id;
  final String changeId;
  final String companyId;
  final String entityType;
  final String entityId;
  final String operation;
  final Map<String, dynamic> payload;
  final int attemptCount;
  final String? lastError;
}
