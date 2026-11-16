import 'dart:convert';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Verifies the RS256 compact token issued by the ERP API.
///
/// The embedded RSA public modulus is intentionally public. The matching
/// private key is never shipped in the client and must remain a server secret.
class ZarrOfflineEntitlement {
  const ZarrOfflineEntitlement({
    required this.companyId,
    required this.productId,
    required this.generation,
    required this.issuedAt,
    required this.offlineExpiresAt,
    required this.licenseExpiresAt,
  });

  final String companyId;
  final String productId;
  final int generation;
  final DateTime issuedAt;
  final DateTime offlineExpiresAt;
  final DateTime? licenseExpiresAt;
}

class ZarrOfflineEntitlementVerifier {
  static const keyId = 'zarr-rs256-2026-01';
  static const _exponent = 65537;

  // RSA-3072 public modulus. Public verification material only.
  static const _modulusHex =
      '824163A2E132E7D34B5F163478A53D8BA8C8E43D80C97459BB7BB6AB74C9A19E'
      'A707D24D7AE5C73CE28A6FC97E62B64468311F3AA1F26EF07838EC250A9BADB'
      '998010C8A9C38B42D30AC10673EF73BD838CFF1F2DCE57993C65935970C2D89'
      'A8EBF94BC02CC59B363CD3438B8F29164DC5AFFEF29DB2FA69F1E353097204FC'
      'DC5C33A1D5F7EB6AE328D06AA31AF901284A1657E7F6981E7B062EB52D6E77F'
      '64DC0F341103C42246CF29251B54DDC906523DBB2CF463E1A65566561BDB977A'
      '91A83A959E74E7303C6D077FA299EDA759525AE86C66D5D02B035BDE24ECE830'
      'DDB2F1E7673FF176F4BFB92FB5C7BE7A436D69B27D4D947DDA8CD3383DCD336'
      '869BBF75098EE3EBBE8AC85E7284E9AECAEA27C9057E7E008BF0D93440423756'
      'CBC6263B19BC208A7D4288C72118E5E007ADE135CF4065E58579FB4251BF02D'
      'B6BA01BF38F6A4C9598CEC1613CBF369900266910485199A131F08455479BCC'
      'CAAF9F15F6075474C6226DD8EC4C1AC14311940A569A9BF61459835B8F00C1D'
      '7F074C7';

  static Uint8List _base64UrlDecode(String value) {
    final normalized = value.replaceAll('-', '+').replaceAll('_', '/');
    return Uint8List.fromList(base64.decode(
      normalized.padRight((normalized.length + 3) ~/ 4 * 4, '='),
    ));
  }

  static Uint8List _hexDecode(String value) {
    final bytes = <int>[];
    for (var i = 0; i < value.length; i += 2) {
      bytes.add(int.parse(value.substring(i, i + 2), radix: 16));
    }
    return Uint8List.fromList(bytes);
  }

  static bool _constantTimeEquals(List<int> a, List<int> b) {
    if (a.length != b.length) return false;
    var difference = 0;
    for (var i = 0; i < a.length; i++) {
      difference |= a[i] ^ b[i];
    }
    return difference == 0;
  }

  static bool _verifyRs256(String signingInput, Uint8List signature) {
    const modulusBytes = 3072 ~/ 8;
    if (signature.length != modulusBytes) return false;

    final modulus = BigInt.parse(_modulusHex, radix: 16);
    final signatureInteger = BigInt.parse(
      signature.map((b) => b.toRadixString(16).padLeft(2, '0')).join(),
      radix: 16,
    );
    if (signatureInteger >= modulus) return false;

    final decodedInteger = signatureInteger.modPow(
      BigInt.from(_exponent),
      modulus,
    );
    final encodedHex = decodedInteger.toRadixString(16).padLeft(
      modulusBytes * 2,
      '0',
    );
    final encoded = _hexDecode(encodedHex);
    final digest = sha256.convert(utf8.encode(signingInput)).bytes;
    const digestInfoPrefix =
        '3031300d060960864801650304020105000420';
    final digestInfo = _hexDecode(digestInfoPrefix + digest
        .map((b) => b.toRadixString(16).padLeft(2, '0')).join());
    final paddingLength = modulusBytes - digestInfo.length - 3;
    if (paddingLength < 8) return false;

    final expected = Uint8List(modulusBytes);
    expected[0] = 0;
    expected[1] = 1;
    for (var i = 0; i < paddingLength; i++) {
      expected[2 + i] = 0xff;
    }
    expected[2 + paddingLength] = 0;
    expected.setRange(3 + paddingLength, modulusBytes, digestInfo);
    return _constantTimeEquals(encoded, expected);
  }

  static ZarrOfflineEntitlement verify(
    String compactToken, {
    required String expectedCompanyId,
    DateTime? now,
  }) {
    final parts = compactToken.split('.');
    if (parts.length != 3) {
      throw const FormatException('ساختار مجوز آفلاین معتبر نیست.');
    }

    final header = jsonDecode(utf8.decode(_base64UrlDecode(parts[0])));
    final claims = jsonDecode(utf8.decode(_base64UrlDecode(parts[1])));
    if (header is! Map<String, dynamic> ||
        claims is! Map<String, dynamic> ||
        header['alg'] != 'RS256' ||
        header['typ'] != 'JWT' ||
        header['kid'] != keyId) {
      throw const FormatException('سرآیند مجوز آفلاین معتبر نیست.');
    }

    final signingInput = '${parts[0]}.${parts[1]}';
    if (!_verifyRs256(signingInput, _base64UrlDecode(parts[2]))) {
      throw const FormatException('امضای مجوز آفلاین معتبر نیست.');
    }

    final current = (now ?? DateTime.now()).toUtc();
    final nowSeconds = current.millisecondsSinceEpoch ~/ 1000;
    final issued = claims['iat'];
    final expires = claims['exp'];
    final generation = claims['generation'];
    final companyId = claims['company_id'];
    final licenseExpiry = claims['license_expires_at'];

    if (claims['iss'] != 'https://kaliia.soft' ||
        claims['aud'] != 'zarr' ||
        claims['product_id'] != 'zarr' ||
        claims['status'] != 'active' ||
        claims['can_write'] != true ||
        companyId != expectedCompanyId ||
        issued is! int ||
        expires is! int ||
        generation is! int ||
        generation < 1 ||
        expires <= nowSeconds ||
        issued > nowSeconds + 300 ||
        expires - issued > const Duration(days: 7).inSeconds ||
        (licenseExpiry != null &&
            (licenseExpiry is! int ||
                expires > licenseExpiry ||
                nowSeconds >= licenseExpiry))) {
      throw const FormatException('مجوز آفلاین منقضی یا نامعتبر است.');
    }

    return ZarrOfflineEntitlement(
      companyId: companyId as String,
      productId: 'zarr',
      generation: generation,
      issuedAt: DateTime.fromMillisecondsSinceEpoch(
        issued * 1000,
        isUtc: true,
      ),
      offlineExpiresAt: DateTime.fromMillisecondsSinceEpoch(
        expires * 1000,
        isUtc: true,
      ),
      licenseExpiresAt: licenseExpiry == null
          ? null
          : DateTime.fromMillisecondsSinceEpoch(
              (licenseExpiry as int) * 1000,
              isUtc: true,
            ),
    );
  }
}

/// Persists only a verified token in platform-protected secure storage.
class ZarrOfflineEntitlementStore {
  ZarrOfflineEntitlementStore({
    FlutterSecureStorage? storage,
  }) : _storage = storage ?? const FlutterSecureStorage();

  static const _tokenPrefix = 'zarr_signed_offline_entitlement_';
  static const _knownPrefix = 'zarr_license_status_known_';
  static const _enforcedPrefix = 'zarr_license_enforced_';
  final FlutterSecureStorage _storage;

  String _tokenKey(String companyId) => _tokenPrefix + companyId;
  String _knownKey(String companyId) => _knownPrefix + companyId;
  String _enforcedKey(String companyId) => _enforcedPrefix + companyId;

  Future<ZarrOfflineEntitlement> saveAndVerify(
    String token, {
    required String companyId,
    DateTime? now,
  }) async {
    final entitlement = ZarrOfflineEntitlementVerifier.verify(
      token,
      expectedCompanyId: companyId,
      now: now,
    );
    await _storage.write(key: _tokenKey(companyId), value: token);
    return entitlement;
  }

  /// Records a fresh server decision for this company. Unknown status is never
  /// treated as an entitlement; once enforcement is enabled, no token means
  /// local writes are denied while reads, export and backup remain available.
  Future<void> updateFromServer({
    required String companyId,
    required bool enforcementEnabled,
    String? token,
    DateTime? now,
  }) async {
    await _storage.write(key: _knownKey(companyId), value: 'true');
    await _storage.write(
      key: _enforcedKey(companyId),
      value: enforcementEnabled ? 'true' : 'false',
    );
    if (token == null || token.isEmpty) {
      await _storage.delete(key: _tokenKey(companyId));
      return;
    }
    try {
      await saveAndVerify(token, companyId: companyId, now: now);
    } on FormatException {
      await _storage.delete(key: _tokenKey(companyId));
    }
  }

  Future<ZarrOfflineEntitlement?> readVerified({
    required String companyId,
    DateTime? now,
  }) async {
    final token = await _storage.read(key: _tokenKey(companyId));
    if (token == null || token.isEmpty) return null;
    try {
      return ZarrOfflineEntitlementVerifier.verify(
        token,
        expectedCompanyId: companyId,
        now: now,
      );
    } on FormatException {
      await _storage.delete(key: _tokenKey(companyId));
      return null;
    }
  }

  Future<void> markUnknown(String companyId) async {
    await _storage.delete(key: _knownKey(companyId));
    await _storage.delete(key: _tokenKey(companyId));
    await _storage.delete(key: _enforcedKey(companyId));
  }

  Future<bool> canWrite({
    required String companyId,
    DateTime? now,
  }) async {
    final known = await _storage.read(key: _knownKey(companyId));
    if (known != 'true') return false;
    final enforced = await _storage.read(key: _enforcedKey(companyId));
    if (enforced != 'true') return true;
    return await readVerified(companyId: companyId, now: now) != null;
  }

  Future<void> clear({String? companyId}) async {
    if (companyId != null) {
      await _storage.delete(key: _tokenKey(companyId));
      await _storage.delete(key: _knownKey(companyId));
      await _storage.delete(key: _enforcedKey(companyId));
      return;
    }
    final keys = await _storage.readAll();
    for (final key in keys.keys) {
      if (key.startsWith(_tokenPrefix) ||
          key.startsWith(_knownPrefix) ||
          key.startsWith(_enforcedPrefix)) {
        await _storage.delete(key: key);
      }
    }
  }
}
