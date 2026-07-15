#!/bin/sh
# Derives SAIC_REST_URI from SAIC_REGION for known endpoints, unless SAIC_REST_URI
# is already set explicitly (so a user can always override for a region not listed
# here). Source of truth: the "API Endpoints" table in the saic-python-mqtt-gateway
# README (SAIC-iSmart-API/saic-python-mqtt-gateway).
if [ -z "${SAIC_REST_URI:-}" ]; then
    case "${SAIC_REGION:-eu}" in
        eu) export SAIC_REST_URI="https://gateway-mg-eu.soimt.com/api.app/v1/" ;;
        au) export SAIC_REST_URI="https://gateway-mg-au.soimt.com/api.app/v1/" ;;
        tr) export SAIC_REST_URI="https://gateway-mg-tr.soimt.com/api.app/v1/" ;;
    esac
fi
