import 'dart:convert';
import 'dart:io';

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
