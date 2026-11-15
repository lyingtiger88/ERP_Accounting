import 'dart:convert';
import 'dart:io';

/// Server-authoritative Kaliia Soft entitlement client.
/// The caller supplies the current verified Supabase access token; never pass
/// a service-role key to this class or ship one in a client build.
class KaliiaEntitlement {
  const KaliiaEntitlement({
    required this.productId,
    required this.status,
    required this.canWrite,
    required this.trialAvailable,
    required this.daysRemaining,
    this.expiresAt,
    this.checkedAt,
  });

  final String productId;
  final String status;
  final bool canWrite;
  final bool trialAvailable;
  final int daysRemaining;
  final DateTime? expiresAt;
  final DateTime? checkedAt;

  factory KaliiaEntitlement.fromJson(Map<String, dynamic> json) {
    DateTime? date(dynamic value) =>
        value is String ? DateTime.tryParse(value)?.toUtc() : null;
    return KaliiaEntitlement(
      productId: json['product_id'] as String? ?? '',
      status: json['status'] as String? ?? 'unknown',
      canWrite: json['can_write'] == true,
      trialAvailable: json['trial_available'] == true,
      daysRemaining: (json['days_remaining'] as num?)?.toInt() ?? 0,
      expiresAt: date(json['expires_at']),
      checkedAt: date(json['checked_at']),
    );
  }
}

class KaliiaEntitlementService {
  KaliiaEntitlementService({
    required this.accessToken,
    required this.productId,
    this.projectUrl = 'https://yztithydgdvydaktuxlw.supabase.co',
    this.publishableKey = 'sb_publishable_UAKFYbxgWOCVRDDdv_0JLg_oIIiubmT',
  });

  final Future<String> Function() accessToken;
  final String productId;
  final String projectUrl;
  final String publishableKey;

  Future<KaliiaEntitlement> _call(String action) async {
    if (!const {'maal', 'shopmanager', 'zurvan'}.contains(productId)) {
      throw ArgumentError.value(productId, 'productId');
    }
    final token = await accessToken();
    if (token.isEmpty) throw StateError('A verified account is required.');
    final client = HttpClient()..connectionTimeout = const Duration(seconds: 12);
    try {
      final request = await client.postUrl(
        Uri.parse('$projectUrl/functions/v1/kaliia-entitlement'),
      );
      request.headers.set(HttpHeaders.contentTypeHeader, 'application/json');
      request.headers.set('apikey', publishableKey);
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
      request.add(utf8.encode(jsonEncode({
        'action': action,
        'product_id': productId,
      })));
      final response = await request.close();
      final body = await utf8.decoder.bind(response).join();
      final decoded = jsonDecode(body);
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw HttpException('Entitlement service rejected request (${response.statusCode}).');
      }
      if (decoded is! Map<String, dynamic> ||
          decoded['entitlement'] is! Map<String, dynamic>) {
        throw const FormatException('Invalid entitlement response.');
      }
      final result = KaliiaEntitlement.fromJson(
        decoded['entitlement'] as Map<String, dynamic>,
      );
      if (result.productId != productId) {
        throw const FormatException('Entitlement product mismatch.');
      }
      return result;
    } finally {
      client.close(force: true);
    }
  }

  Future<KaliiaEntitlement> refresh() => _call('status');
  Future<KaliiaEntitlement> startTrial() => _call('start_trial');
}

/// Use at the domain/service boundary before every mutating command.
/// Read-only operations should not call this guard.
void requireKaliiaWrite(KaliiaEntitlement entitlement) {
  if (!entitlement.canWrite) {
    throw StateError('اشتراک پایان یافته است؛ اطلاعات فقط قابل مشاهده هستند.');
  }
}
