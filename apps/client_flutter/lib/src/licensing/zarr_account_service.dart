import 'dart:convert';
import 'dart:io';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'kaliia_entitlement.dart';

/// Independent Kaliia Soft identity: the ERP API's custom JWT is NEVER a
/// Supabase Auth JWT, so account/entitlement requests use this separate session.
class ZarrAccountService {
  ZarrAccountService({
    FlutterSecureStorage? storage,
    this.projectUrl = 'https://yztithydgdvydaktuxlw.supabase.co',
    this.publishableKey = 'sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT',
  }) : _storage = storage ?? const FlutterSecureStorage();

  static const productId = 'zarr';
  static const _accessKey = 'zarr_kaliia_access_token';
  static const _refreshKey = 'zarr_kaliia_refresh_token';
  static const _emailKey = 'zarr_kaliia_email';
  final FlutterSecureStorage _storage;
  final String projectUrl;
  final String publishableKey;

  Future<Map<String, dynamic>> _authRequest(
      String grant, Map<String, dynamic> payload) async {
    final http = HttpClient()..connectionTimeout = const Duration(seconds: 15);
    try {
      final request = await http.postUrl(
        Uri.parse('$projectUrl/auth/v1/token?grant_type=$grant'),
      );
      request.headers.set(HttpHeaders.contentTypeHeader, 'application/json');
      request.headers.set('apikey', publishableKey);
      request.add(utf8.encode(jsonEncode(payload)));
      final response = await request.close();
      final body = await utf8.decoder.bind(response).join();
      final parsed = jsonDecode(body);
      if (response.statusCode < 200 || response.statusCode >= 300 ||
          parsed is! Map<String, dynamic>) {
        throw StateError('ورود به حساب کالیا سافت ناموفق بود.');
      }
      if (parsed['access_token'] is! String ||
          parsed['refresh_token'] is! String) {
        throw const FormatException('پاسخ احراز هویت معتبر نیست.');
      }
      return parsed;
    } finally {
      http.close(force: true);
    }
  }

  Future<void> _saveSession(Map<String, dynamic> session, String email) async {
    await _storage.write(key: _accessKey, value: session['access_token'] as String);
    await _storage.write(key: _refreshKey, value: session['refresh_token'] as String);
    await _storage.write(key: _emailKey, value: email);
  }

  Future<void> signIn(String email, String password) async {
    final clean = email.trim().toLowerCase();
    if (clean.isEmpty || password.isEmpty) {
      throw ArgumentError('ایمیل و رمز عبور الزامی است.');
    }
    final session = await _authRequest('password', {
      'email': clean,
      'password': password,
    });
    await _saveSession(session, clean);
  }

  Future<String> _validToken() async {
    final refresh = await _storage.read(key: _refreshKey);
    if (refresh == null || refresh.isEmpty) {
      throw StateError('ابتدا به حساب کالیا سافت وارد شوید.');
    }
    // Rotate refresh tokens: Supabase sessions have short-lived access tokens.
    final session = await _authRequest('refresh_token', {
      'refresh_token': refresh,
    });
    await _saveSession(session, await accountEmail() ?? '');
    return session['access_token'] as String;
  }

  /// A fresh Supabase access JWT for one-time owner-verification requests.
  /// Never attach this to the ERP application's ordinary API requests.
  Future<String> verifiedKaliiaAccessToken() => _validToken();

  Future<String?> accountEmail() => _storage.read(key: _emailKey);

  Future<KaliiaEntitlement> status() async {
    final token = await _validToken();
    return KaliiaEntitlementService(
      accessToken: () async => token,
      productId: productId,
      projectUrl: projectUrl,
      publishableKey: publishableKey,
    ).refresh();
  }

  /// Trial starts only on explicit request. The trial is never reset locally.
  Future<KaliiaEntitlement> startTrial() async {
    final token = await _validToken();
    return KaliiaEntitlementService(
      accessToken: () async => token,
      productId: productId,
      projectUrl: projectUrl,
      publishableKey: publishableKey,
    ).startTrial();
  }

  Future<void> signOut() async {
    await _storage.delete(key: _accessKey);
    await _storage.delete(key: _refreshKey);
    await _storage.delete(key: _emailKey);
  }
}
