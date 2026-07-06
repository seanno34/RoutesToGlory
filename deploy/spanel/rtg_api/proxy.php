<?php
/**
 * Forward /rtg_api/* to the Node API on localhost.
 * Upload to: public_html/rtg_api/proxy.php
 */
declare(strict_types=1);

$port = 3001;
$uri = $_SERVER['REQUEST_URI'] ?? '/';
$path = parse_url($uri, PHP_URL_PATH) ?? '/';
$path = preg_replace('#^/rtg_api#', '', $path) ?: '/';

$url = "http://127.0.0.1:{$port}{$path}";
$query = $_SERVER['QUERY_STRING'] ?? '';
if ($query !== '') {
    $url .= '?' . $query;
}

$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
$headers = ['Accept: application/json'];
$contentType = $_SERVER['CONTENT_TYPE'] ?? $_SERVER['HTTP_CONTENT_TYPE'] ?? '';
if ($contentType !== '') {
    $headers[] = 'Content-Type: ' . $contentType;
}

$ch = curl_init($url);
curl_setopt_array($ch, [
    CURLOPT_RETURNTRANSFER => true,
    CURLOPT_CUSTOMREQUEST => $method,
    CURLOPT_HTTPHEADER => $headers,
    CURLOPT_TIMEOUT => 120,
]);

if (in_array($method, ['POST', 'PUT', 'PATCH', 'DELETE'], true)) {
    curl_setopt($ch, CURLOPT_POSTFIELDS, file_get_contents('php://input') ?: '');
}

$body = curl_exec($ch);
if ($body === false) {
    http_response_code(503);
    header('Content-Type: application/json');
    echo json_encode([
        'error' => 'Node API unreachable on port ' . $port,
        'detail' => curl_error($ch),
        'hint' => 'SPanel → NodeJS Manager → rtg_api → Restart, then View Logs',
    ]);
    exit;
}

$code = (int) curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

http_response_code($code);
header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

if ($method === 'OPTIONS') {
    exit;
}

echo $body;
