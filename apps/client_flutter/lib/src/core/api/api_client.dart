import 'dart:convert';
import 'dart:io';

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

  Future<dynamic> _request(
    String method,
    String path, {
    Map<String, dynamic>? body,
    String? bearerToken,
  }) async {
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
