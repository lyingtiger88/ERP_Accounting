import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class SecureSession {
  const SecureSession({
    required this.accessToken,
    required this.accessExpiresAt,
    required this.refreshToken,
    required this.refreshExpiresAt,
    required this.userId,
    required this.companyId,
    required this.displayName,
    required this.role,
    required this.mfaRequired,
  });

  final String accessToken;
  final DateTime accessExpiresAt;
  final String refreshToken;
  final DateTime refreshExpiresAt;
  final String userId;
  final String companyId;
  final String displayName;
  final String role;
  final bool mfaRequired;
}

class SecureSessionStore {
  SecureSessionStore._();

  static final SecureSessionStore instance = SecureSessionStore._();

  static const _storage = FlutterSecureStorage(
    aOptions: AndroidOptions(
      encryptedSharedPreferences: true,
    ),
  );

  static const _accessToken = 'erp.access_token';
  static const _accessExpiresAt = 'erp.access_expires_at';
  static const _refreshToken = 'erp.refresh_token';
  static const _refreshExpiresAt = 'erp.refresh_expires_at';
  static const _userId = 'erp.user_id';
  static const _companyId = 'erp.company_id';
  static const _displayName = 'erp.display_name';
  static const _role = 'erp.role';
  static const _mfaRequired = 'erp.mfa_required';

  SecureSession? _memory;

  Future<void> write(SecureSession session) async {
    _memory = session;

    await Future.wait([
      _storage.write(key: _accessToken, value: session.accessToken),
      _storage.write(
        key: _accessExpiresAt,
        value: session.accessExpiresAt.toUtc().toIso8601String(),
      ),
      _storage.write(key: _refreshToken, value: session.refreshToken),
      _storage.write(
        key: _refreshExpiresAt,
        value: session.refreshExpiresAt.toUtc().toIso8601String(),
      ),
      _storage.write(key: _userId, value: session.userId),
      _storage.write(key: _companyId, value: session.companyId),
      _storage.write(key: _displayName, value: session.displayName),
      _storage.write(key: _role, value: session.role),
      _storage.write(
        key: _mfaRequired,
        value: session.mfaRequired ? '1' : '0',
      ),
    ]);
  }

  Future<SecureSession?> read() async {
    final cached = _memory;
    if (cached != null) {
      return cached;
    }

    final values = await Future.wait([
      _storage.read(key: _accessToken),
      _storage.read(key: _accessExpiresAt),
      _storage.read(key: _refreshToken),
      _storage.read(key: _refreshExpiresAt),
      _storage.read(key: _userId),
      _storage.read(key: _companyId),
      _storage.read(key: _displayName),
      _storage.read(key: _role),
      _storage.read(key: _mfaRequired),
    ]);

    if (values.take(8).any((value) => value == null)) {
      return null;
    }

    final accessExpiry = DateTime.tryParse(values[1]!);
    final refreshExpiry = DateTime.tryParse(values[3]!);

    if (accessExpiry == null || refreshExpiry == null) {
      await clear();
      return null;
    }

    final session = SecureSession(
      accessToken: values[0]!,
      accessExpiresAt: accessExpiry,
      refreshToken: values[2]!,
      refreshExpiresAt: refreshExpiry,
      userId: values[4]!,
      companyId: values[5]!,
      displayName: values[6]!,
      role: values[7]!,
      mfaRequired: values[8] == '1',
    );
    _memory = session;
    return session;
  }

  Future<String?> currentAccessToken() async =>
      (await read())?.accessToken;

  Future<String?> currentRefreshToken() async =>
      (await read())?.refreshToken;

  Future<void> clear() async {
    _memory = null;
    await Future.wait([
      _storage.delete(key: _accessToken),
      _storage.delete(key: _accessExpiresAt),
      _storage.delete(key: _refreshToken),
      _storage.delete(key: _refreshExpiresAt),
      _storage.delete(key: _userId),
      _storage.delete(key: _companyId),
      _storage.delete(key: _displayName),
      _storage.delete(key: _role),
      _storage.delete(key: _mfaRequired),
    ]);
  }
}
