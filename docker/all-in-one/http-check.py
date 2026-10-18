#!/usr/bin/env python3
"""Exits 0 when the URL given as the only argument answers with a 2xx status.

Otherwise prints one line saying why and exits 1, like `curl -fsS`. The image has Python anyway
(supervisord and the SAIC gateway run on it) but no curl. Used by the HEALTHCHECK and
wait-for-api.sh.
"""

import sys
import urllib.request

url = sys.argv[1]
try:
    # urlopen raises for any 4xx/5xx answer as well as for connection errors and timeouts.
    with urllib.request.urlopen(url, timeout=4):
        pass
except Exception as error:  # any failure means "not ready"; report which one
    sys.exit(f"{url}: {error}")
