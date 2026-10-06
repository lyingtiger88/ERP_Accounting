import 'dart:convert';
import 'dart:io';

import '../demo/demo_mode.dart';
import '../sync/sync_models.dart';

class ApiException implements Exception {
  const ApiException(this.message, [this.statusCode]);

  final String message;
  final int? statusCode;

  @override
  String toString() => message;
}

class LoginResult {
  const LoginResult({
    required this.accessToken,
    required this.expiresAt,
    required this.userId,
    required this.companyId,
    required this.displayName,
    required this.role,
    required this.mfaRequired,
  });

  final String accessToken;
  final DateTime expiresAt;
  final String userId;
  final String companyId;
  final String displayName;
  final String role;
  final bool mfaRequired;

  factory LoginResult.fromJson(Map<String, dynamic> json) {
    return LoginResult(
      accessToken: json['accessToken'] as String,
      expiresAt: DateTime.parse(json['expiresAt'] as String),
      userId: json['userId'] as String,
      companyId: json['companyId'] as String,
      displayName: json['displayName'] as String,
      role: json['role'] as String,
      mfaRequired: json['mfaRequired'] as bool? ?? false,
    );
  }
}

class JournalSyncResult {
  const JournalSyncResult({
    required this.journalEntryId,
    required this.number,
    required this.status,
    required this.postedAt,
    required this.duplicate,
  });

  final String journalEntryId;
  final String number;
  final String status;
  final DateTime? postedAt;
  final bool duplicate;

  factory JournalSyncResult.fromJson(Map<String, dynamic> json) {
    return JournalSyncResult(
      journalEntryId: json['journalEntryId'] as String,
      number: json['number'] as String,
      status: json['status'].toString(),
      postedAt: json['postedAt'] == null
          ? null
          : DateTime.parse(json['postedAt'] as String),
      duplicate: json['duplicate'] as bool? ?? false,
    );
  }
}

class DetailAccountSyncResult {
  const DetailAccountSyncResult({
    required this.outcome,
    required this.entity,
    required this.serverConflict,
    required this.baseRevision,
    required this.duplicate,
  });

  final String outcome;
  final Map<String, dynamic>? entity;
  final Map<String, dynamic>? serverConflict;
  final int? baseRevision;
  final bool duplicate;

  bool get applied => outcome == 'Applied';
  bool get conflict => outcome == 'Conflict';

  factory DetailAccountSyncResult.fromJson(
    Map<String, dynamic> json,
  ) {
    final conflict = json['conflict'] as Map?;

    return DetailAccountSyncResult(
      outcome: json['outcome'].toString(),
      entity: json['entity'] == null
          ? null
          : Map<String, dynamic>.from(json['entity'] as Map),
      serverConflict: conflict?['server'] == null
          ? null
          : Map<String, dynamic>.from(
              conflict!['server'] as Map,
            ),
      baseRevision:
          (conflict?['baseRevision'] as num?)?.toInt(),
      duplicate: json['duplicate'] as bool? ?? false,
    );
  }
}

class ApiClient {
  ApiClient({String? baseUrl}) : baseUrl = baseUrl ?? _defaultBaseUrl();

  final String baseUrl;

  static String _defaultBaseUrl() {
    const configured = String.fromEnvironment('API_BASE_URL');
    if (configured.isNotEmpty) {
      return configured;
    }

    if (Platform.isAndroid) {
      return 'http://10.0.2.2:5000';
    }

    return 'http://127.0.0.1:5000';
  }

  Future<void> bootstrap({
    required String companyName,
    required String username,
    required String displayName,
    required String password,
  }) async {
    await _request(
      'POST',
      '/api/auth/bootstrap',
      body: {
        'companyName': companyName,
        'username': username,
        'displayName': displayName,
        'password': password,
      },
    );
  }

  Future<LoginResult> login({
    required String username,
    required String password,
    required String deviceName,
  }) async {
    final payload = await _request(
      'POST',
      '/api/auth/login',
      body: {
        'username': username,
        'password': password,
        'deviceName': deviceName,
        'platform': Platform.operatingSystem,
      },
    );

    return LoginResult.fromJson(payload as Map<String, dynamic>);
  }

  Future<List<Map<String, dynamic>>> getCurrencies({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/currencies',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createCurrency({
    required String bearerToken,
    required String code,
    required String name,
    String? symbol,
    int decimalPlaces = 2,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currencies',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'symbol': symbol,
        'decimalPlaces': decimalPlaces,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> setCurrencyState({
    required String bearerToken,
    required String currencyId,
    required bool isActive,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currencies/' +
          currencyId +
          '/state',
      bearerToken: bearerToken,
      body: {
        'isActive': isActive,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> setBaseCurrency({
    required String bearerToken,
    required String currencyId,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currencies/base',
      bearerToken: bearerToken,
      body: {
        'currencyId': currencyId,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<List<Map<String, dynamic>>> getCurrencyRates({
    required String bearerToken,
    String? currencyId,
    DateTime? from,
    DateTime? to,
    int limit = 300,
  }) async {
    final parts = <String>[
      if (currencyId != null && currencyId.isNotEmpty)
        'currencyId=' + Uri.encodeQueryComponent(currencyId),
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
      'limit=' + limit.toString(),
    ];

    final payload = await _request(
      'GET',
      '/api/accounting/currency-rates?' +
          parts.join('&'),
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> saveCurrencyRate({
    required String bearerToken,
    required String currencyId,
    required DateTime rateDate,
    required num buyRate,
    required num sellRate,
    required num accountingRate,
    String source = 'Manual',
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currency-rates',
      bearerToken: bearerToken,
      body: {
        'currencyId': currencyId,
        'rateDate': _dateOnly(rateDate),
        'buyRate': buyRate,
        'sellRate': sellRate,
        'accountingRate': accountingRate,
        'source': source,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<num> getCurrencyAccountingRate({
    required String bearerToken,
    required String currencyId,
    required DateTime date,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/currencies/' +
          currencyId +
          '/accounting-rate?date=' +
          _dateOnly(date),
      bearerToken: bearerToken,
    );

    return (payload as Map)['accountingRate'] as num;
  }

  Future<Map<String, dynamic>> createForeignCurrencyJournal({
    required String bearerToken,
    required String currencyId,
    required DateTime documentDate,
    required String description,
    required List<Map<String, dynamic>> lines,
    String? fiscalYearId,
    num? exchangeRate,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currency-journals',
      bearerToken: bearerToken,
      body: {
        'currencyId': currencyId,
        'documentDate': _dateOnly(documentDate),
        'description': description,
        'lines': lines,
        'fiscalYearId': fiscalYearId,
        'exchangeRate': exchangeRate,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> getCurrencyPosition({
    required String bearerToken,
    required DateTime asOf,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/currency-position?asOf=' +
          _dateOnly(asOf),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> postCurrencyRevaluation({
    required String bearerToken,
    required DateTime asOf,
    required String gainAccountId,
    required String lossAccountId,
    String? fiscalYearId,
    String? description,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/currency-revaluation',
      bearerToken: bearerToken,
      body: {
        'asOf': _dateOnly(asOf),
        'gainAccountId': gainAccountId,
        'lossAccountId': lossAccountId,
        'fiscalYearId': fiscalYearId,
        'description': description,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<List<Map<String, dynamic>>> getAccounts({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/accounts',
      bearerToken: bearerToken,
    );

    final items = payload as List<dynamic>;

    return items
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<List<Map<String, dynamic>>> getFiscalYears({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/fiscal-years',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<List<Map<String, dynamic>>> getCostCenters({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/cost-centers',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createCostCenter({
    required String bearerToken,
    required String code,
    required String name,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/cost-centers',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> updateCostCenter({
    required String bearerToken,
    required String costCenterId,
    required String code,
    required String name,
    required bool isActive,
  }) async {
    final payload = await _request(
      'PUT',
      '/api/accounting/cost-centers/' + costCenterId,
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'isActive': isActive,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<List<Map<String, dynamic>>> getAccountingProjects({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/projects',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createAccountingProject({
    required String bearerToken,
    required String code,
    required String name,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/projects',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> updateAccountingProject({
    required String bearerToken,
    required String projectId,
    required String code,
    required String name,
    required bool isActive,
  }) async {
    final payload = await _request(
      'PUT',
      '/api/accounting/projects/' + projectId,
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'isActive': isActive,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<List<Map<String, dynamic>>> getDetailAccounts({
    required String bearerToken,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/detail-accounts',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createFiscalYear({
    required String bearerToken,
    required String name,
    required int persianYear,
    required DateTime startDate,
    required DateTime endDate,
    required bool isDefault,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-years',
      bearerToken: bearerToken,
      body: {
        'name': name,
        'persianYear': persianYear,
        'startDate': _dateOnly(startDate),
        'endDate': _dateOnly(endDate),
        'isDefault': isDefault,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<List<Map<String, dynamic>>> getFiscalPeriods({
    required String bearerToken,
    required String fiscalYearId,
  }) async {
    final payload = await _request(
      'GET',
      '/api/accounting/fiscal-years/' +
          fiscalYearId +
          '/periods',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<List<Map<String, dynamic>>> ensureStandardFiscalPeriods({
    required String bearerToken,
    required String fiscalYearId,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-years/' +
          fiscalYearId +
          '/periods/ensure-standard',
      bearerToken: bearerToken,
    );

    return (payload as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> setFiscalPeriodClosed({
    required String bearerToken,
    required String periodId,
    required bool isClosed,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-periods/' +
          periodId +
          '/state',
      bearerToken: bearerToken,
      body: {
        'isClosed': isClosed,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> finalizeFiscalYear({
    required String bearerToken,
    required String fiscalYearId,
    required String retainedEarningsAccountId,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-years/' +
          fiscalYearId +
          '/finalize',
      bearerToken: bearerToken,
      body: {
        'retainedEarningsAccountId':
            retainedEarningsAccountId,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> reopenFinalizedFiscalYear({
    required String bearerToken,
    required String fiscalYearId,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-years/' +
          fiscalYearId +
          '/reopen-finalized',
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> setFiscalYearClosed({
    required String bearerToken,
    required String fiscalYearId,
    required bool isClosed,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/fiscal-years/' + fiscalYearId + '/state',
      bearerToken: bearerToken,
      body: {
        'isClosed': isClosed,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  Future<Map<String, dynamic>> createDetailAccount({
    required String bearerToken,
    required String code,
    required String name,
    required String type,
    String? nationalId,
  }) async {
    final payload = await _request(
      'POST',
      '/api/accounting/detail-accounts',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'type': type,
        'nationalId': nationalId,
      },
    );

    return Map<String, dynamic>.from(payload as Map);
  }

  static String _dateOnly(DateTime value) {
    final year = value.year.toString().padLeft(4, '0');
    final month = value.month.toString().padLeft(2, '0');
    final day = value.day.toString().padLeft(2, '0');
    return year + '-' + month + '-' + day;
  }

  Future<DetailAccountSyncResult> syncDetailAccount({
    required String bearerToken,
    required String changeId,
    required Map<String, dynamic> payload,
  }) async {
    final response = await _request(
      'POST',
      '/api/accounting/sync/detail-account',
      bearerToken: bearerToken,
      body: {
        'changeId': changeId,
        'entityId': payload['entityId'],
        'code': payload['code'],
        'name': payload['name'],
        'type': payload['type'],
        'nationalId': payload['nationalId'],
        'isActive': payload['isActive'],
        'baseRevision': payload['baseRevision'],
      },
    );

    return DetailAccountSyncResult.fromJson(
      Map<String, dynamic>.from(response as Map),
    );
  }

  Future<JournalSyncResult> syncJournal({
    required String bearerToken,
    required String changeId,
    required Map<String, dynamic> payload,
  }) async {
    final fiscalYearId = payload['fiscalYearId'] as String?;
    final localDocumentId = payload['localDocumentId'] as String?;

    if (fiscalYearId == null || fiscalYearId.isEmpty) {
      throw const ApiException(
        'سند محلی سال مالی ندارد و قابل همگام‌سازی نیست.',
      );
    }

    if (localDocumentId == null || localDocumentId.isEmpty) {
      throw const ApiException(
        'شناسه سند محلی برای همگام‌سازی موجود نیست.',
      );
    }

    final response = await _request(
      'POST',
      '/api/accounting/sync-journal',
      bearerToken: bearerToken,
      body: {
        'changeId': changeId,
        'localDocumentId': localDocumentId,
        'fiscalYearId': fiscalYearId,
        'documentDate': payload['documentDate'],
        'description': payload['description'],
        'lines': payload['lines'],
      },
    );

    return JournalSyncResult.fromJson(
      Map<String, dynamic>.from(response as Map),
    );
  }

  Future<DetailAccountPullPage> pullDetailAccountChanges({
    required String bearerToken,
    required int afterCursor,
    int limit = 100,
  }) async {
    final response = await _request(
      'GET',
      '/api/accounting/sync/detail-accounts?after=' +
          afterCursor.toString() +
          '&limit=' +
          limit.toString(),
      bearerToken: bearerToken,
    );

    return DetailAccountPullPage.fromJson(
      Map<String, dynamic>.from(response as Map),
    );
  }

  Future<Map<String, dynamic>> reverseJournal({
    required String bearerToken,
    required String journalEntryId,
    required DateTime documentDate,
    required String reason,
    String? fiscalYearId,
  }) async {
    final response = await _request(
      'POST',
      '/api/accounting/journals/' +
          journalEntryId +
          '/reverse',
      bearerToken: bearerToken,
      body: {
        'documentDate': _dateOnly(documentDate),
        'reason': reason,
        'fiscalYearId': fiscalYearId,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<JournalPullPage> pullJournalChanges({
    required String bearerToken,
    required int afterCursor,
    int limit = 100,
  }) async {
    final response = await _request(
      'GET',
      '/api/accounting/sync/journals?after=' +
          afterCursor.toString() +
          '&limit=' +
          limit.toString(),
      bearerToken: bearerToken,
    );

    return JournalPullPage.fromJson(
      Map<String, dynamic>.from(response as Map),
    );
  }

  Future<List<Map<String, dynamic>>> getJournals({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
  }) async {
    final query = _reportQuery(from: from, to: to);

    final response = await _request(
      'GET',
      '/api/accounting/journals' +
          (query.isEmpty ? '' : '?' + query),
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> getReportsCenter({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
    String? warehouseId,
    String? productId,
    String? detailAccountId,
  }) async {
    final parts = <String>[
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
      if (warehouseId != null && warehouseId.isNotEmpty)
        'warehouseId=' + Uri.encodeQueryComponent(warehouseId),
      if (productId != null && productId.isNotEmpty)
        'productId=' + Uri.encodeQueryComponent(productId),
      if (detailAccountId != null && detailAccountId.isNotEmpty)
        'detailAccountId=' + Uri.encodeQueryComponent(detailAccountId),
    ];

    final response = await _request(
      'GET',
      '/api/reports/center' +
          (parts.isEmpty ? '' : '?' + parts.join('&')),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<int>> getReportsCenterCsv({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
    String? warehouseId,
    String? productId,
    String? detailAccountId,
  }) async {
    final parts = <String>[
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
      if (warehouseId != null && warehouseId.isNotEmpty)
        'warehouseId=' + Uri.encodeQueryComponent(warehouseId),
      if (productId != null && productId.isNotEmpty)
        'productId=' + Uri.encodeQueryComponent(productId),
      if (detailAccountId != null && detailAccountId.isNotEmpty)
        'detailAccountId=' + Uri.encodeQueryComponent(detailAccountId),
    ];

    final client = HttpClient();
    try {
      final request = await client.getUrl(
        Uri.parse(
          baseUrl +
              '/api/reports/center.csv' +
              (parts.isEmpty ? '' : '?' + parts.join('&')),
        ),
      );
      request.headers.set(
        HttpHeaders.authorizationHeader,
        'Bearer ' + bearerToken,
      );
      final response = await request.close();
      final bytes = await response.fold<List<int>>(
        <int>[],
        (buffer, chunk) => buffer..addAll(chunk),
      );

      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw ApiException(
          utf8.decode(bytes, allowMalformed: true),
          response.statusCode,
        );
      }
      return bytes;
    } on SocketException {
      throw const ApiException(
        'اتصال به سرور برقرار نشد. آدرس API و اجرای سرور را بررسی کنید.',
      );
    } finally {
      client.close(force: true);
    }
  }

  Future<Map<String, dynamic>> getGeneralLedger({
    required String bearerToken,
    String? accountId,
    DateTime? from,
    DateTime? to,
  }) async {
    final parts = <String>[
      if (accountId != null && accountId.isNotEmpty)
        'accountId=' + Uri.encodeQueryComponent(accountId),
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
    ];

    final response = await _request(
      'GET',
      '/api/accounting/general-ledger' +
          (parts.isEmpty ? '' : '?' + parts.join('&')),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> getDetailLedger({
    required String bearerToken,
    required String detailAccountId,
    DateTime? from,
    DateTime? to,
  }) async {
    final parts = <String>[
      'detailAccountId=' +
          Uri.encodeQueryComponent(detailAccountId),
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
    ];

    final response = await _request(
      'GET',
      '/api/accounting/detail-ledger?' +
          parts.join('&'),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> getProfitLoss({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
  }) async {
    final query = _reportQuery(from: from, to: to);

    final response = await _request(
      'GET',
      '/api/accounting/profit-loss' +
          (query.isEmpty ? '' : '?' + query),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> getBalanceSheet({
    required String bearerToken,
    required DateTime asOf,
  }) async {
    final response = await _request(
      'GET',
      '/api/accounting/balance-sheet?asOf=' +
          _dateOnly(asOf),
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getTrialBalance({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
  }) async {
    final query = _reportQuery(from: from, to: to);

    final response = await _request(
      'GET',
      '/api/accounting/trial-balance' +
          (query.isEmpty ? '' : '?' + query),
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  static String _reportQuery({
    DateTime? from,
    DateTime? to,
  }) {
    return <String>[
      if (from != null) 'from=' + _dateOnly(from),
      if (to != null) 'to=' + _dateOnly(to),
    ].join('&');
  }

  Future<List<Map<String, dynamic>>> getAccountingAudit({
    required String bearerToken,
    String? entityId,
    int limit = 100,
  }) async {
    final query = <String>[
      'limit=' + limit.toString(),
      if (entityId != null && entityId.isNotEmpty)
        'entityId=' + Uri.encodeQueryComponent(entityId),
    ].join('&');

    final response = await _request(
      'GET',
      '/api/accounting/audit?' + query,
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<void> ensureSalesInventoryDefaults({
    required String bearerToken,
  }) async {
    await _request(
      'POST',
      '/api/sales-inventory/defaults/ensure',
      bearerToken: bearerToken,
    );
  }

  Future<List<Map<String, dynamic>>> getTreasuryAccounts({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/treasury/accounts',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createTreasuryAccount({
    required String bearerToken,
    required String code,
    required String name,
    required String type,
    required String ledgerAccountId,
    String? currencyId,
  }) async {
    final response = await _request(
      'POST',
      '/api/treasury/accounts',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'type': type,
        'ledgerAccountId': ledgerAccountId,
        'currencyId': currencyId,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> updateTreasuryAccount({
    required String bearerToken,
    required String accountId,
    required String code,
    required String name,
    required String type,
    required String ledgerAccountId,
    String? currencyId,
    required bool isActive,
  }) async {
    final response = await _request(
      'PUT',
      '/api/treasury/accounts/' + accountId,
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'type': type,
        'ledgerAccountId': ledgerAccountId,
        'currencyId': currencyId,
        'isActive': isActive,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getTreasuryTransactions({
    required String bearerToken,
    DateTime? from,
    DateTime? to,
  }) async {
    final query = <String>[
      if (from != null) 'from=' + Uri.encodeQueryComponent(_dateOnly(from)),
      if (to != null) 'to=' + Uri.encodeQueryComponent(_dateOnly(to)),
    ];

    final response = await _request(
      'GET',
      '/api/treasury/transactions' +
          (query.isEmpty ? '' : '?' + query.join('&')),
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map((item) => Map<String, dynamic>.from(item as Map))
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> postTreasuryTransaction({
    required String bearerToken,
    required String fiscalYearId,
    required DateTime documentDate,
    required String type,
    required num amount,
    String? description,
    String? fromTreasuryAccountId,
    String? toTreasuryAccountId,
    String? counterAccountId,
    String? detailAccountId,
    String? currencyId,
    num? exchangeRate,
  }) async {
    final response = await _request(
      'POST',
      '/api/treasury/transactions',
      bearerToken: bearerToken,
      body: {
        'fiscalYearId': fiscalYearId,
        'documentDate': _dateOnly(documentDate),
        'type': type,
        'amount': amount,
        'description': description,
        'fromTreasuryAccountId': fromTreasuryAccountId,
        'toTreasuryAccountId': toTreasuryAccountId,
        'counterAccountId': counterAccountId,
        'detailAccountId': detailAccountId,
        'currencyId': currencyId,
        'exchangeRate': exchangeRate,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getStoreProducts({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/products',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createStoreProduct({
    required String bearerToken,
    required String sku,
    required String name,
    String? barcode,
    required String unitName,
    required String kind,
    required bool trackInventory,
    required num salesPrice,
    required num defaultPurchasePrice,
    String trackingMode = 'None',
    num minimumStock = 0,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/products',
      bearerToken: bearerToken,
      body: {
        'sku': sku,
        'name': name,
        'barcode': barcode,
        'unitName': unitName,
        'kind': kind,
        'trackInventory': trackInventory,
        'salesPrice': salesPrice,
        'defaultPurchasePrice': defaultPurchasePrice,
        'trackingMode': trackingMode,
        'minimumStock': minimumStock,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> updateStoreProduct({
    required String bearerToken,
    required String productId,
    required String sku,
    required String name,
    String? barcode,
    required String unitName,
    required String kind,
    required bool trackInventory,
    required num salesPrice,
    required num defaultPurchasePrice,
    required bool isActive,
    String trackingMode = 'None',
    num minimumStock = 0,
  }) async {
    final response = await _request(
      'PUT',
      '/api/sales-inventory/products/' + productId,
      bearerToken: bearerToken,
      body: {
        'sku': sku,
        'name': name,
        'barcode': barcode,
        'unitName': unitName,
        'kind': kind,
        'trackInventory': trackInventory,
        'salesPrice': salesPrice,
        'defaultPurchasePrice': defaultPurchasePrice,
        'isActive': isActive,
        'trackingMode': trackingMode,
        'minimumStock': minimumStock,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getWarehouses({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/warehouses',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createWarehouse({
    required String bearerToken,
    required String code,
    required String name,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/warehouses',
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> updateWarehouse({
    required String bearerToken,
    required String warehouseId,
    required String code,
    required String name,
    required bool isActive,
  }) async {
    final response = await _request(
      'PUT',
      '/api/sales-inventory/warehouses/' + warehouseId,
      bearerToken: bearerToken,
      body: {
        'code': code,
        'name': name,
        'isActive': isActive,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getStockBalances({
    required String bearerToken,
    String? warehouseId,
  }) async {
    final query = warehouseId == null || warehouseId.isEmpty
        ? ''
        : '?warehouseId=' + Uri.encodeQueryComponent(warehouseId);

    final response = await _request(
      'GET',
      '/api/sales-inventory/stock' + query,
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> adjustStock({
    required String bearerToken,
    required String warehouseId,
    required String productId,
    required DateTime documentDate,
    required num quantityDelta,
    num? unitCost,
    required String reason,
    String? lotNumber,
    String? serialNumber,
    DateTime? expiryDate,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/stock/adjust',
      bearerToken: bearerToken,
      body: {
        'warehouseId': warehouseId,
        'productId': productId,
        'documentDate': _dateOnly(documentDate),
        'quantityDelta': quantityDelta,
        'unitCost': unitCost,
        'reason': reason,
        'lotNumber': lotNumber,
        'serialNumber': serialNumber,
        'expiryDate': expiryDate == null ? null : _dateOnly(expiryDate),
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>?> findStoreProduct({
    required String bearerToken,
    required String code,
  }) async {
    try {
      final response = await _request(
        'GET',
        '/api/sales-inventory/products/lookup?code=' +
            Uri.encodeQueryComponent(code),
        bearerToken: bearerToken,
      );

      return Map<String, dynamic>.from(response as Map);
    } on ApiException catch (error) {
      if (error.statusCode == 404) return null;
      rethrow;
    }
  }

  Future<List<Map<String, dynamic>>> getLowStockAlerts({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/stock/low',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<List<Map<String, dynamic>>> getStockTraceBalances({
    required String bearerToken,
    String? warehouseId,
    String? productId,
  }) async {
    final parts = <String>[
      if (warehouseId != null && warehouseId.isNotEmpty)
        'warehouseId=' + Uri.encodeQueryComponent(warehouseId),
      if (productId != null && productId.isNotEmpty)
        'productId=' + Uri.encodeQueryComponent(productId),
    ];

    final response = await _request(
      'GET',
      '/api/sales-inventory/stock/trace' +
          (parts.isEmpty ? '' : '?' + parts.join('&')),
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<List<Map<String, dynamic>>> getPurchaseOrders({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/purchase-orders',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createPurchaseOrder({
    required String bearerToken,
    required String fiscalYearId,
    required DateTime documentDate,
    DateTime? expectedDate,
    required String supplierDetailAccountId,
    required String warehouseId,
    String? description,
    required List<Map<String, dynamic>> lines,
    String? currencyId,
    num? exchangeRate,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/purchase-orders',
      bearerToken: bearerToken,
      body: {
        'fiscalYearId': fiscalYearId,
        'documentDate': _dateOnly(documentDate),
        'expectedDate':
            expectedDate == null ? null : _dateOnly(expectedDate),
        'supplierDetailAccountId': supplierDetailAccountId,
        'warehouseId': warehouseId,
        'description': description,
        'lines': lines,
        'currencyId': currencyId,
        'exchangeRate': exchangeRate,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> setPurchaseOrderStatus({
    required String bearerToken,
    required String orderId,
    required String status,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/purchase-orders/' +
          orderId +
          '/status',
      bearerToken: bearerToken,
      body: {'status': status},
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getPurchaseReceipts({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/purchases',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createPurchaseReceipt({
    required String bearerToken,
    required String fiscalYearId,
    required DateTime documentDate,
    required String warehouseId,
    String? supplierDetailAccountId,
    required String paymentType,
    String? description,
    required List<Map<String, dynamic>> lines,
    String? currencyId,
    num? exchangeRate,
    String? purchaseOrderId,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/purchases',
      bearerToken: bearerToken,
      body: {
        'fiscalYearId': fiscalYearId,
        'documentDate': _dateOnly(documentDate),
        'warehouseId': warehouseId,
        'supplierDetailAccountId': supplierDetailAccountId,
        'paymentType': paymentType,
        'description': description,
        'lines': lines,
        'currencyId': currencyId,
        'exchangeRate': exchangeRate,
        'purchaseOrderId': purchaseOrderId,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> postPurchaseReceipt({
    required String bearerToken,
    required String receiptId,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/purchases/' + receiptId + '/post',
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getPurchaseReturns({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/purchase-returns',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createPurchaseReturn({
    required String bearerToken,
    required String receiptId,
    required DateTime documentDate,
    required String reason,
    required List<Map<String, dynamic>> lines,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/purchases/' +
          receiptId +
          '/returns',
      bearerToken: bearerToken,
      body: {
        'documentDate': _dateOnly(documentDate),
        'reason': reason,
        'lines': lines,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getWarehouseTransfers({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/transfers',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createWarehouseTransfer({
    required String bearerToken,
    required DateTime documentDate,
    required String fromWarehouseId,
    required String toWarehouseId,
    String? description,
    required List<Map<String, dynamic>> lines,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/transfers',
      bearerToken: bearerToken,
      body: {
        'documentDate': _dateOnly(documentDate),
        'fromWarehouseId': fromWarehouseId,
        'toWarehouseId': toWarehouseId,
        'description': description,
        'lines': lines,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> postWarehouseTransfer({
    required String bearerToken,
    required String transferId,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/transfers/' + transferId + '/post',
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getSalesReturns({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/returns',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createSalesReturn({
    required String bearerToken,
    required String invoiceId,
    required DateTime documentDate,
    required String reason,
    required List<Map<String, dynamic>> lines,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/invoices/' +
          invoiceId +
          '/returns',
      bearerToken: bearerToken,
      body: {
        'documentDate': _dateOnly(documentDate),
        'reason': reason,
        'lines': lines,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> getSalesInventorySettings({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/settings',
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> updateSalesInventorySettings({
    required String bearerToken,
    required Map<String, dynamic> settings,
  }) async {
    final response = await _request(
      'PUT',
      '/api/sales-inventory/settings',
      bearerToken: bearerToken,
      body: settings,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<List<Map<String, dynamic>>> getSalesInvoices({
    required String bearerToken,
  }) async {
    final response = await _request(
      'GET',
      '/api/sales-inventory/invoices',
      bearerToken: bearerToken,
    );

    return (response as List<dynamic>)
        .map(
          (item) => Map<String, dynamic>.from(item as Map),
        )
        .toList(growable: false);
  }

  Future<Map<String, dynamic>> createSalesInvoice({
    required String bearerToken,
    required String fiscalYearId,
    required DateTime documentDate,
    required String warehouseId,
    String? customerDetailAccountId,
    required String paymentType,
    String? description,
    required List<Map<String, dynamic>> lines,
    String? currencyId,
    num? exchangeRate,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/invoices',
      bearerToken: bearerToken,
      body: {
        'fiscalYearId': fiscalYearId,
        'documentDate': _dateOnly(documentDate),
        'warehouseId': warehouseId,
        'customerDetailAccountId': customerDetailAccountId,
        'paymentType': paymentType,
        'description': description,
        'lines': lines,
        'currencyId': currencyId,
        'exchangeRate': exchangeRate,
      },
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<Map<String, dynamic>> postSalesInvoice({
    required String bearerToken,
    required String invoiceId,
  }) async {
    final response = await _request(
      'POST',
      '/api/sales-inventory/invoices/' + invoiceId + '/post',
      bearerToken: bearerToken,
    );

    return Map<String, dynamic>.from(response as Map);
  }

  Future<dynamic> _request(
    String method,
    String path, {
    Map<String, dynamic>? body,
    String? bearerToken,
  }) async {
    if (DemoMode.isDemoToken(bearerToken)) {
      throw const ApiException(
        'حالت تست آفلاین است و به سرور متصل نمی‌شود.',
      );
    }

    final client = HttpClient();

    try {
      final request = await client.openUrl(
        method,
        Uri.parse(baseUrl + path),
      );

      request.headers.contentType = ContentType.json;

      if (bearerToken != null) {
        request.headers.set(
          HttpHeaders.authorizationHeader,
          'Bearer ' + bearerToken,
        );
      }

      if (body != null) {
        request.write(jsonEncode(body));
      }

      final response = await request.close();
      final responseText = await response.transform(utf8.decoder).join();
      final dynamic decoded =
          responseText.isEmpty ? null : jsonDecode(responseText);

      if (response.statusCode < 200 || response.statusCode >= 300) {
        String message =
            'Request failed (' + response.statusCode.toString() + ')';

        if (decoded is Map<String, dynamic> && decoded['error'] is String) {
          message = decoded['error'] as String;
        }

        throw ApiException(message, response.statusCode);
      }

      return decoded;
    } on SocketException {
      throw const ApiException(
        'اتصال به سرور برقرار نشد. آدرس API و اجرای سرور را بررسی کنید.',
      );
    } finally {
      client.close(force: true);
    }
  }
}
