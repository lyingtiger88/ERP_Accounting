import 'package:flutter_test/flutter_test.dart';
import 'package:zarr/src/licensing/kaliia_entitlement.dart';
import 'package:zarr/src/licensing/zarr_account_service.dart';

void main() {
  test('Zarr has a distinct product identifier and cannot consume Maal license', () {
    expect(ZarrAccountService.productId, 'zarr');
    expect(ZarrAccountService.productId, isNot('maal'));
    final maal = KaliiaEntitlement.fromJson({
      'product_id': 'maal',
      'status': 'active',
      'can_write': true,
    });
    final zarr = KaliiaEntitlement.fromJson({
      'product_id': ZarrAccountService.productId,
      'status': 'trial_available',
      'can_write': false,
      'trial_available': true,
    });
    expect(maal.productId, isNot(zarr.productId));
    expect(zarr.canWrite, isFalse);
    expect(zarr.trialAvailable, isTrue);
  });


  group('KaliiaEntitlement', () {
    test('parses an active server entitlement', () {
      final value = KaliiaEntitlement.fromJson({
        'product_id': 'maal',
        'status': 'active',
        'can_write': true,
        'trial_available': false,
        'days_remaining': 45,
        'expires_at': '2026-12-30T12:00:00Z',
        'checked_at': '2026-11-15T12:00:00Z',
      });
      expect(value.productId, 'maal');
      expect(value.status, 'active');
      expect(value.canWrite, isTrue);
      expect(value.daysRemaining, 45);
      expect(value.expiresAt, isNotNull);
    });

    test('defaults missing write permission to false', () {
      final value = KaliiaEntitlement.fromJson({
        'product_id': 'maal',
        'status': 'unknown',
      });
      expect(value.canWrite, isFalse);
      expect(value.trialAvailable, isFalse);
      expect(value.daysRemaining, 0);
    });

    test('rejects mutations when expired', () {
      final value = KaliiaEntitlement.fromJson({
        'product_id': 'maal',
        'status': 'expired',
        'can_write': false,
      });
      expect(() => requireKaliiaWrite(value), throwsStateError);
    });

    test('allows mutations only when server grants can_write', () {
      final value = KaliiaEntitlement.fromJson({
        'product_id': 'maal',
        'status': 'active',
        'can_write': true,
      });
      expect(() => requireKaliiaWrite(value), returnsNormally);
    });
  });
}
