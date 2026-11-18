import 'package:flutter_test/flutter_test.dart';

import '../lib/src/core/app_version.dart';

void main() {
  test('Zarr version is shared by Windows and Android', () {
    expect(ZarrAppVersion.name, '0.5.0-beta.3');
    expect(ZarrAppVersion.build, 7);
    expect(ZarrAppVersion.display, '0.5.0-beta.3 (build 7)');
    expect(ZarrAppVersion.platforms, 'Windows • Android');
  });
}
