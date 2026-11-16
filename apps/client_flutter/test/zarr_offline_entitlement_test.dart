import 'package:flutter_test/flutter_test.dart';
import 'package:erp_accounting/src/licensing/zarr_offline_entitlement.dart';

void main() {
  const token = 'eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6InphcnQtdGVzdC1maXh0dXJlLTIwMjYtMTEifQ.eyJpc3MiOiJodHRwczovL2thbGlpYS5zb2Z0IiwiYXVkIjoiemFyciIsInN1YiI6IjExMTExMTExLTExMTEtNDExMS04MTExLTExMTExMTExMTExMSIsImNvbXBhbnlfaWQiOiIxMTExMTExMS0xMTExLTQxMTEtODExMS0xMTExMTExMTExMTEiLCJwcm9kdWN0X2lkIjoiemFyciIsInN0YXR1cyI6ImFjdGl2ZSIsImNhbl93cml0ZSI6dHJ1ZSwiZ2VuZXJhdGlvbiI6MywiaWF0IjoxNzk0ODE5NjAwLCJleHAiOjE3OTQ5MDYwMDAsImxpY2Vuc2VfZXhwaXJlc19hdCI6MTc5NDkwNjAwMH0.ZkhIDtWm9M-8_aWJwFUMtpIyoG3gfcgNgUiLpnhPJyWaVGdtoyEQpgm4-gFKt7bwwZDlj69gOiGXW0tyjsQsxtQUDclf-vplRqBjZ6vWwqJAVSWaoBSWE8YP4xqatI9GvXYEDf7m1vK3IEtBMYumVRFlke6V6qR6Ppg05Ty7k7FUgLdjnTgeD8H9Nziob5onlfDdLh6hrX5aAJIK_RJklgtuE7VVg_GlNqo4FouqzTIBL0Thq-smv77ZMYmN5LjgdqMvpkl0pXa6ZrLLVsqU1UnHZOX6yE90Hy4rrerIhB8zVq6beS9bChiWS2vmVP6ZGBxGmeTToLUpTzjqtQ6gdPoMjcvrkLmYOM3nYcKx1WrG1ahcNyyjy9UAGBDtDP2pgK01ZQLXRrTj2fVrikdGCvEYLuFBRbfKERTnH2tSLYjs53d4k15Tc9nnxzfiGuZQW5zlbeIj-4oMuQEOBQmbbMb6PEKasSktbGqamEW6Bu79qVgNcT_ipenHlFnX8abp';
  const company = '11111111-1111-4111-8111-111111111111';

  group('ZarrOfflineEntitlementVerifier', () {
    test('accepts a valid company-bound RS256 token within its seven-day window', () {
      final entitlement = ZarrOfflineEntitlementVerifier.verify(
        token,
        expectedCompanyId: company,
        now: DateTime.utc(2026, 11, 16, 9, 5),
      );

      expect(entitlement.productId, 'zarr');
      expect(entitlement.companyId, company);
      expect(entitlement.generation, 3);
      expect(entitlement.offlineExpiresAt, DateTime.utc(2026, 11, 17, 9));
    });

    test('rejects a token presented for another company', () {
      expect(
        () => ZarrOfflineEntitlementVerifier.verify(
          token,
          expectedCompanyId: '22222222-2222-4222-8222-222222222222',
          now: DateTime.utc(2026, 11, 16, 9, 5),
        ),
        throwsFormatException,
      );
    });

    test('rejects a modified signature', () {
      final modified = token.substring(0, token.length - 1) +
          (token.endsWith('A') ? 'B' : 'A');
      expect(
        () => ZarrOfflineEntitlementVerifier.verify(
          modified,
          expectedCompanyId: company,
          now: DateTime.utc(2026, 11, 16, 9, 5),
        ),
        throwsFormatException,
      );
    });

    test('rejects expired offline grace', () {
      expect(
        () => ZarrOfflineEntitlementVerifier.verify(
          token,
          expectedCompanyId: company,
          now: DateTime.utc(2026, 11, 18),
        ),
        throwsFormatException,
      );
    });
  });
}
