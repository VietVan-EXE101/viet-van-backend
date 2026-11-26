# Hướng dẫn FE kết nối API Việt Văn

Cập nhật theo Controllers/DTOs hiện tại ngày **25/11/2026**. Tài liệu dùng cho cả màn hình Customer và Admin, ví dụ bằng JavaScript `fetch`, có thể áp dụng cho React/Vue hoặc FE khác.

Các ID, tên, ngày và số tiền trong ví dụ là dữ liệu minh họa. Khi tích hợp, dùng dữ liệu API trả về. Backend cần được cấu hình và chạy migration tương ứng trước khi thử các luồng có database. Hướng dẫn vận hành Backend nằm trong [README_DRAMA.md](../README_DRAMA.md).

## Đọc nhanh: nên làm theo thứ tự nào?

1. Cấu hình địa chỉ API và hàm gọi API dùng chung.
2. Làm đăng ký → xác minh email → đăng nhập → lấy `/api/auth/me`.
3. Làm danh sách/chi tiết kịch → xem video FREE → gửi đánh giá.
4. Làm trang mua gói → tạo đơn → hiển thị QR → kiểm tra trạng thái đơn.
5. Làm player tương tác PRO dựa trên manifest và asset.
6. Làm Admin Studio: tạo draft → upload → lưu manifest → preview → publish.

## 1. Địa chỉ API, token và quyền

| Mục | Giá trị/cách dùng |
|---|---|
| API local theo launch profile hiện có | `http://localhost:5080` |
| Swagger local, trong Development | `http://localhost:5080/swagger` |
| Đường dẫn endpoint | Đã có tiền tố `/api`, ví dụ `/api/dramas` |
| JSON | Gửi `Content-Type: application/json` |
| Upload | Dùng `FormData`; để trình duyệt tự đặt Content-Type và multipart boundary |
| API cần đăng nhập | Header `Authorization: Bearer <token>` |
| JSON response | Tên field dạng camelCase: `fullName`, `hasFreeVideo`, `bankAccount` |
| Response thành công | Có thể là object, array hoặc object phân trang; **không có wrapper `data` chung** |

Nếu FE chạy ở port/domain khác, BE cần cấu hình `Cors:AllowedOrigins` đúng origin FE. Google Login cần gửi cookie bằng `credentials: "include"`; FE/API phải cùng site hoặc dùng reverse proxy vì cookie challenge hiện là `SameSite=Strict`. Địa chỉ deploy lấy từ môi trường FE, không cố định localhost trong bản phát hành.

**Phân biệt role và quyền nội dung:**

| Giá trị | Ý nghĩa |
|---|---|
| `role === "Customer"` | Người dùng thường |
| `role === "Admin"` | Quản trị, có quyền quản lý và preview kịch mà không cần mua PRO |
| `accessLevel === "FREE"` trong `/api/auth/me` | Quyền subscription hiện tại là FREE |
| `accessLevel === "PRO"` trong `/api/auth/me` | Có subscription trả phí còn hiệu lực |

Không kiểm tra `role === "PRO"`. Không dùng `subscriptionTierLevel` cũ trong JWT để tự quyết định quyền hiện tại. Admin có thể có `accessLevel: "FREE"` trong profile nhưng vẫn được preview nội dung PRO.

Ứng dụng hiện chưa có refresh-token API. Khi token hết hạn/bị thu hồi, yêu cầu đăng nhập lại. Logout thu hồi **mọi phiên** của tài khoản.

## 2. Hàm gọi API dùng chung

Ví dụ dưới giữ token trong bộ nhớ. Nếu dùng auth store sẵn có, thay `getAccessToken`/`setAccessToken` bằng store của FE. Lưu trong bộ nhớ nghĩa là tải lại trang sẽ cần đăng nhập lại; chọn cơ chế duy trì phiên cùng thiết kế bảo mật của FE.

```javascript
export const API_BASE_URL = "http://localhost:5080";
let accessToken = null;

export const getAccessToken = () => accessToken;
export const setAccessToken = (value) => { accessToken = value || null; };

export function apiUrl(path) {
  const base = new URL(API_BASE_URL);
  const url = new URL(path, base);
  if (url.origin !== base.origin) throw new Error("URL không thuộc API Việt Văn");
  return url.toString();
}

export class ApiError extends Error {
  constructor(status, payload) {
    const validation = Object.values(payload?.errors ?? {}).flat().join("\n");
    super(payload?.message || validation || payload?.title || `HTTP ${status}`);
    this.status = status;
    this.code = payload?.code;
    this.errors = payload?.errors;
    this.traceId = payload?.traceId;
  }
}

export async function api(path, { auth = true, json, ...options } = {}) {
  const headers = new Headers(options.headers);
  const token = getAccessToken();
  if (auth && token) headers.set("Authorization", `Bearer ${token}`);
  if (json !== undefined) {
    headers.set("Content-Type", "application/json");
    options.body = JSON.stringify(json);
  }

  const response = await fetch(apiUrl(path), {
    ...options,
    headers,
    credentials: "include"
  });
  const payload = response.status === 204 ? null
    : response.headers.get("content-type")?.includes("json")
      ? await response.json() : await response.text();

  if (!response.ok) {
    if (response.status === 401 && auth && token) setAccessToken(null);
    throw new ApiError(response.status, payload);
  }
  return payload;
}
```

Các đoạn ví dụ tiếp theo dùng chung các hàm trên. API binary như video/ảnh/audio dùng `fetch` riêng ở mục 4 và 7, không dùng hàm này để đọc JSON.

## 3. Tài khoản: đăng ký, đăng nhập và Google

### FE gọi API nhưng chưa nhận email (cập nhật 27/11/2026)

FE gọi Backend tại `https://viet-van-backend.onrender.com`; Backend gọi dịch vụ email. Không đưa API key Resend vào FE. Luồng quên mật khẩu từ Render đã có bằng chứng provider chấp nhận và event Delivered cho email chủ tài khoản Resend; kết quả này không chứng minh email gửi được tới mọi người dùng.

Nếu sender trên Render vẫn là `onboarding@resend.dev` và chưa có domain Verified, Resend chỉ cho gửi tới email chủ tài khoản Resend. Đổi sang email người dùng khác trong FE không vượt qua được giới hạn này. Để gửi cho người dùng thực: xác minh domain trong Resend, sau đó cấu hình `Email__Resend__FromAddress` trên Render bằng địa chỉ thuộc domain đó. Xem [giới hạn sender thử của Resend](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain).

| Kết quả FE quan sát | Cách hiểu và bước tiếp theo |
| --- | --- |
| Register 200, `verificationEmailSent: false` | Tài khoản đã tạo nhưng lần gửi chưa thành công. Không đăng ký lại; kiểm tra log Backend/provider, dùng gửi lại sau khi xử lý nguyên nhân. |
| Register 200, `verificationEmailSent: true` | Provider chấp nhận thư; kiểm tra Resend → Emails và Inbox/Spam, chưa phải xác nhận người dùng đã đọc thư. |
| Resend-verification/forgot-password 200 | Thông báo chung, không đảm bảo có tài khoản đủ điều kiện hoặc thư được gửi. Đối chiếu log đúng endpoint và thời điểm. |
| HTTP 400 | Xem `errors` trong response để sửa JSON/field/email/password. |
| HTTP 401 trên API auth công khai | Kiểm tra FE có tự gắn JWT cũ/hết hạn không; dùng `auth: false` trong helper ở mục 2 cho các request này. |
| HTTP 429 hoặc log Cooldown | Chờ ít nhất 60 giây trước khi gửi lại; không gửi liên tục tự động. |
| Browser báo CORS/OPTIONS thất bại | Kiểm tra origin FE trong `Cors__AllowedOrigins__0` (hoặc index tiếp theo), không kèm path/trailing slash. Xác minh POST thực đã tới Backend; không suy ra thư đã gửi từ trạng thái giao diện. |

Khi báo lỗi, gửi cho BE **Request URL, HTTP status, response JSON, thời điểm và email nhận có trùng chủ Resend không**. Không gửi Authorization header, API key, mật khẩu hoặc token xác minh/reset. Register/resend-verification dùng log `EmailSend...`/`EmailVerification...`; forgot-password dùng `PasswordResetEmail...`. Chưa có Network/log request của FE thực tế thì chưa kết luận nguyên nhân là FE, CORS hay giới hạn provider. Không sửa trạng thái xác minh trong database để vượt qua bước này.

### 3.1. Đăng ký và xác minh email

```javascript
const registration = await api("/api/auth/register", {
  method: "POST",
  auth: false,
  json: {
    email: "reader@example.com",
    fullName: "Nguyễn Văn An",
    password: "ExamplePass123!"
  }
});
```

Response `200` minh họa:

```json
{
  "userId": 8,
  "username": "user_generated_by_server",
  "role": "Customer",
  "subscriptionTierLevel": 0,
  "token": "",
  "fullName": "Nguyễn Văn An",
  "emailVerificationRequired": true,
  "verificationEmailSent": true
}
```

- `fullName` tối đa 150 ký tự; email tối đa 150; mật khẩu 8–128 ký tự.
- `username` cũ vẫn được nhận nếu FE cần; khi gửi phải dài 3–50 ký tự. Cần họ tên hoặc username.
- `token` rỗng khi đăng ký: chuyển sang màn hình yêu cầu xác minh, chưa vào trạng thái đã đăng nhập.
- Nếu `verificationEmailSent === false`, tài khoản vẫn đã được tạo. Hiển thị chức năng gửi lại, không lặp POST register để gửi thư.

Backend hiện gửi link dạng `https://<backend>/verify-email.html#<token>`. Trang có sẵn xử lý xác minh. Nếu BE được cấu hình chuyển link về trang FE riêng, FE lấy token từ fragment và gọi:

```javascript
const token = decodeURIComponent(window.location.hash.slice(1));
await api("/api/auth/verify-email", {
  method: "POST", auth: false, json: { token }
});
```

Link có hạn một giờ, dùng một lần. Sau thành công, chuyển về trang đăng nhập; API xác minh không trả access token.

Gửi lại email:

```javascript
await api("/api/auth/resend-verification", {
  method: "POST", auth: false, json: { email: "reader@example.com" }
});
```

Nút gửi lại nên có đếm ngược ít nhất 60 giây. Response dùng thông báo chung, không chứng minh email tồn tại hay thư đã tới hộp thư.

### 3.2. Đăng nhập và lấy quyền hiện tại

```javascript
const login = await api("/api/auth/login", {
  method: "POST", auth: false,
  json: { usernameOrEmail: "reader@example.com", password: "ExamplePass123!" }
});
setAccessToken(login.token);
const me = await api("/api/auth/me");
const canUseInteractive = me.role === "Admin" || me.accessLevel === "PRO";
```

Login trả cùng kiểu AuthResponse như đăng ký, nhưng `token` có giá trị. `me` có dạng:

```json
{
  "id": 8,
  "username": "user_generated_by_server",
  "fullName": "Nguyễn Văn An",
  "email": "reader@example.com",
  "role": "Customer",
  "createdAt": "2026-11-25T02:00:00",
  "accessLevel": "FREE",
  "proUntil": null
}
```

Dùng `me.id`, không phải `me.userId` — `userId` là field của response login/register. Login có thể trả 401 do sai thông tin, email chưa xác minh hoặc tài khoản bị khóa. Hiển thị thông báo server; không tự đoán nguyên nhân chỉ từ status.

### 3.3. Quên mật khẩu và logout

| API | Body | FE làm tiếp |
|---|---|---|
| `POST /api/auth/forgot-password` | `{ "email": "reader@example.com" }` | Hiển thị thông báo chung “Nếu tài khoản hợp lệ, email hướng dẫn sẽ được gửi” |
| `POST /api/auth/reset-password` | `{ "token": "<mã trong email hoặc token từ fragment>", "password": "NewExamplePass123!" }` | Thành công thì xóa phiên cũ và về đăng nhập |
| `POST /api/auth/logout` | Không cần body, cần Bearer | Trả 204; xóa token, profile và cache media |

Email chứa mã reset 43 ký tự, sống 30 phút và chỉ dùng một lần; mã này là giá trị `token` của API reset. Nếu BE cấu hình `Email:PasswordResetUrl`, email kèm link tới trang FE với cùng token sau `#`. Không có URL FE thì vẫn gửi mã, có thể kiểm thử qua Swagger hoặc dùng form nhập mã. BE đổi mật khẩu xong sẽ thu hồi JWT cũ.

Nếu forgot-password trả `503` với `code: "PASSWORD_RESET_NOT_CONFIGURED"`, URL trang reset đã cấu hình ở BE không hợp lệ; bỏ trống URL là hợp lệ với luồng gửi mã. FE hiển thị thông báo dịch vụ và gửi traceId cho BE. Khi API trả 200, giữ thông báo chung; 200 không chứng minh email đã vào Gmail. Nếu người dùng yêu cầu gửi lại, chờ tối thiểu một phút. BE có log `PasswordResetEmailAcceptedByProvider` / `PasswordResetEmailFailed` để kiểm tra SMTP.

```javascript
try {
  await api("/api/auth/logout", { method: "POST" });
} finally {
  setAccessToken(null);
  // Đồng thời xóa auth store và giải phóng object URL của player.
}
```

Nếu request logout lỗi mạng, việc xóa token cục bộ không chứng minh các phiên khác đã được server thu hồi.

### 3.4. Đăng nhập hoặc liên kết Google

**Cấu hình localhost đã kiểm tra ngày 26/11/2026:** FE `http://localhost:3001`, trang đăng ký `http://localhost:3001/register`, BE `http://localhost:5080`. Trong môi trường `Development`, BE đã cho phép origin FE kèm credentials qua `appsettings.Development.json`. `/register` là đường dẫn trang, không đưa vào CORS origin.

`GoogleSignIn:ClientId` ở User Secrets của BE khớp Client ID FE theo xác nhận của người phát triển. Trong Google Cloud Console, chọn OAuth client **VietVan** loại **Web application** và kiểm tra **Authorized JavaScript origins** có `http://localhost:3001`. Luồng GIS popup/callback bên dưới không dùng `/register` làm redirect URI. Cấu hình Google Console chưa được kiểm tra trực tiếp. Tham khảo [hướng dẫn cấu hình OAuth client của Google](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid).

1. Gọi `POST /api/auth/google/challenge`, `auth: false`. Response có `clientId`, `nonce`; trình duyệt nhận cookie HttpOnly.
2. Khởi tạo Google Sign-In bằng chính clientId/nonce đó; lấy **Google ID token** sau khi người dùng đăng nhập.
3. Gọi `POST /api/auth/google` với `{ "idToken": "<Google ID token>" }`, `auth: false` và cookie như bước 1.
4. Thành công: nhận AuthResponse, lưu token Việt Văn, gọi `/api/auth/me` giống login email.

`credentials: "include"` trong helper đã phục vụ bước 1 và 3. Google token không thay cho Bearer token Việt Văn. Challenge sống 5 phút, chỉ dùng một lần; khi thử lại phải tạo challenge mới.

Ví dụ sau khi SDK `https://accounts.google.com/gsi/client` đã tải và phần tử `google-signin` đã được render:

```javascript
const challenge = await api("/api/auth/google/challenge", {
  method: "POST", auth: false
});
google.accounts.id.initialize({
  client_id: challenge.clientId,
  nonce: challenge.nonce,
  callback: async (response) => {
    try {
      const session = await api("/api/auth/google", {
        method: "POST", auth: false,
        json: { idToken: response.credential }
      });
      setAccessToken(session.token);
      // Cập nhật auth store và chuyển trang theo giao diện FE.
    } catch (error) {
      // Hiển thị lỗi; lần thử tiếp theo phải tạo challenge và khởi tạo lại với nonce mới.
      console.error(error.message);
    }
  }
});
google.accounts.id.renderButton(document.getElementById("google-signin"), {
  theme: "outline", size: "large"
});
```

`response.credential` là **ID Token Google**. Không điền Client ID kết thúc bằng `.apps.googleusercontent.com` vào `idToken`. Không ghi token vào log hoặc URL.

Cookie `vv_google_challenge` là `HttpOnly`, `SameSite=Strict`, path `/api/auth/google`, không đặt Domain; HTTP localhost dùng `Secure=false`, HTTPS dùng `Secure=true`. Hai port localhost trên cùng HTTP là cùng site nhưng khác origin, nên vẫn cần `credentials: "include"` ở cả hai request. Không trộn `localhost` với `127.0.0.1`, hoặc đổi một phía sang HTTPS. HTTP ngoài Development bị từ chối. Backend giữ nguyên kiểm tra chữ ký RS256 bằng khóa Google, audience, issuer, thời hạn, email đã xác minh và nonce; không cần Google Client Secret cho luồng ID Token này. JWT Việt Văn được ký bằng secret riêng lấy từ cấu hình BE.

Nếu Google hiển thị `no registered origin`/`invalid_client`, kiểm tra Authorized JavaScript origins và Client ID trên Google Console. Nếu BE trả 401, tạo challenge mới và kiểm tra cookie, nonce, `response.credential`; không bỏ kiểm tra nonce để xử lý lỗi. Sau khi cập nhật source, khởi động lại BE bằng launch profile `http` để dùng cấu hình Development.

Nếu nhận `409` với `code: "ACCOUNT_LINK_REQUIRED"`: hướng dẫn người dùng đăng nhập tài khoản Việt Văn đang có → tạo Google challenge mới → lấy ID token với nonce mới → gọi `POST /api/auth/google/link` có Bearer, body `{ "idToken": "..." }`. Thành công trả 204. Google email phải phù hợp tài khoản đang liên kết.

## 4. Danh sách kịch, chi tiết và video FREE

### 4.1. Màn hình danh sách và chi tiết

```javascript
const dramas = await api("/api/dramas", { auth: false }); // Array trực tiếp
const drama = await api(`/api/dramas/${dramaId}`);       // Gửi token nếu có
```

Danh sách hiện **chưa phân trang, chưa có search/filter server**. Một item minh họa:

```json
{
  "id": 12,
  "title": "Tên vở kịch",
  "description": "Mô tả",
  "literaryWorkId": null,
  "sceneCount": 0,
  "accessLevel": "Public",
  "status": "PUBLISHED",
  "hasFreeVideo": true
}
```

Chi tiết có các field quan trọng:

| Field | FE sử dụng |
|---|---|
| `hasFreeVideo` | Hiển thị nút xem video FREE |
| `hasInteractive` | Hiển thị lựa chọn tương tác PRO |
| `coverImage` | Có thể null; nếu có, lấy `coverImage.url` ghép với API base |
| `averageRating`, `totalRatings`, `myRating` | Hiển thị thống kê và đánh giá của chính user |
| `assets`, `scenes` | Có thể rỗng; player mới lấy dữ liệu qua `/interactive` |
| `status`, `isPublished` | Trạng thái tác phẩm; Customer chỉ được xem published |

Danh sách không có `coverImage` hoặc `hasInteractive` trong item hiện tại; lấy ở chi tiết nếu màn hình cần. `sceneCount === 0` không có nghĩa là kịch không xem được: video FREE không cần scene. `accessLevel: "Public"` ở drama là field tương thích cũ, không đồng nghĩa user có PRO.

### 4.2. Phát video và ghi nhận lượt mở của user đã đăng nhập

URL video: `GET /api/dramas/{id}/video`. Customer xem video đã xuất bản; Admin cũng xem được video của draft/disabled. Không có video thì 404.

Video FREE là binary, hỗ trợ Range. Dùng URL trực tiếp cho `<video>` để trình duyệt tải/seek theo nhu cầu, không fetch toàn bộ video thành Blob.

`<video src>` không tự gửi header Bearer. Nếu user đã đăng nhập và cần đánh giá sau khi xem, thực hiện request Range nhỏ có Bearer để BE ghi nhận đúng tài khoản đang mở video:

```javascript
async function openFreeVideo(dramaId, videoElement) {
  const url = apiUrl(`/api/dramas/${dramaId}/video`);
  const token = getAccessToken();
  if (token) {
    const response = await fetch(url, {
      headers: { Authorization: `Bearer ${token}`, Range: "bytes=0-0" },
      cache: "no-store"
    });
    if (!response.ok) {
      if (response.status === 401) setAccessToken(null);
      throw new ApiError(response.status, { message: "Không mở được video" });
    }
    if (response.status !== 206) {
      await response.body?.cancel();
      throw new Error("Cần kiểm tra Range ở API/proxy trước khi tải video");
    }
    await response.arrayBuffer(); // Chỉ đọc byte được yêu cầu.
  }
  videoElement.src = url;
  videoElement.controls = true;
  videoElement.preload = "metadata";
}
```

Gọi khi user chọn mở player. Đây là truy cập media thật có xác thực; FE không gửi cờ `isWatched`. User chưa đăng nhập vẫn xem được video nhưng cần đăng nhập và mở nội dung với Bearer để gửi đánh giá.

Admin preview video chưa xuất bản cần player/loader gửi Bearer trên các request media. Không gắn URL draft vào `<video src>` rồi kỳ vọng cookie challenge Google thay cho Bearer. Nếu FE chưa có player hỗ trợ header, phần preview video lớn cần thống nhất thêm cách stream với BE; ảnh/audio nhỏ có cách tải có Bearer ở mục 7.

## 5. Gửi và hiển thị đánh giá

| API | Cách dùng |
|---|---|
| `GET /api/dramas/{id}/reviews?page=1&pageSize=10&sort=newest` | Danh sách public; sort nhận `newest`, `oldest`, `rating` |
| `GET /api/dramas/{id}/reviews/summary` | Điểm trung bình, tổng đánh giá và phân bố sao |
| `PUT /api/dramas/{id}/feedback` | Có Bearer; tạo hoặc cập nhật đánh giá của mình |
| `DELETE /api/dramas/{id}/feedback` | Có Bearer; xóa đánh giá của mình, thành công 204 |

```javascript
const saved = await api(`/api/dramas/${dramaId}/feedback`, {
  method: "PUT", json: { rating: 5, comment: "Vở kịch dễ theo dõi" }
});
// Sau thành công, tải lại chi tiết drama và danh sách reviews để cập nhật điểm.
```

`rating` là số nguyên 1–5. Comment có thể bỏ qua/null/rỗng, tối đa 2000 ký tự. Một user chỉ có một đánh giá trên một drama; không cần gửi userId. BE kiểm tra lượt mở hợp lệ và quyền truy cập hiện tại; chưa mở nội dung có thể trả 409, hết PRO trên kịch chỉ có PRO trả 403. Sửa đánh giá đang Hidden không tự làm nó xuất hiện lại.

Response danh sách:

```json
{
  "items": [{ "id": 21, "rating": 5, "comment": "Hay", "displayName": "Nguyễn Văn An", "isMine": true }],
  "page": 1,
  "pageSize": 10,
  "totalItems": 1,
  "totalPages": 1
}
```

Ví dụ lược bớt timestamps của item. Summary dùng tên `totalReviews`; chi tiết drama dùng `totalRatings`. Response lưu feedback có `id`, `rating`, `comment`, `status`, `createdAt`, `updatedAt`.

## 6. Mua PRO và theo dõi thanh toán

### 6.1. Chọn gói và tạo đơn

1. `GET /api/subscription-packages`: array các gói đang hoạt động. Hiển thị `name`, `price`, `durationInDays`, `description`, `benefits` từ API.
2. Gói có `tierLevel > 0` là gói trả phí cấp PRO khi thanh toán hợp lệ; không cố định chỉ tier 3 mới có PRO. Gói Free không cần tạo đơn.
3. User chọn gói → tạo đơn, chỉ gửi packageId:

```javascript
// Tạo một lần cho mỗi thao tác mua mới, giữ lại nếu phải retry do mạng.
const paymentAttemptKey = crypto.randomUUID();
const order = await api("/api/payments/orders", {
  method: "POST",
  headers: { "Idempotency-Key": paymentAttemptKey },
  json: { packageId: selectedPackage.id }
});
```

`Idempotency-Key` dài 8–100 ký tự chữ/số/`-`/`_`. Retry cùng lần mua dùng **cùng key và packageId**. Lần mua mới dùng key mới. Không gửi amount, status, tier hoặc thời hạn để server tin theo.

Response minh họa:

```json
{
  "orderCode": "VV0123456789ABCDEF",
  "packageName": "Tên gói từ database",
  "amount": 50000,
  "currency": "VND",
  "durationDays": 30,
  "status": "Pending",
  "createdAt": "2026-11-25T02:00:00Z",
  "expiresAt": "2026-11-25T02:15:00Z",
  "paidAt": null,
  "qrUrl": "https://img.vietqr.io/image/EXAMPLE.png",
  "transferContent": "VV0123456789ABCDEF",
  "bankName": "Tên ngân hàng từ cấu hình",
  "bankAccount": "Số tài khoản từ cấu hình",
  "accountHolder": "Tên chủ tài khoản từ cấu hình",
  "reviewReason": null,
  "packageId": 2
}
```

QR/số tiền trên chỉ minh họa cấu trúc, không dùng để chuyển tiền. Field response là **`bankAccount`**, không phải `accountNumber`. `qrUrl` là URL ảnh ngoài API, có thể dùng trực tiếp cho `<img>`; không gửi nó vào helper `apiUrl` chỉ chấp nhận origin BE.

### 6.2. Kiểm tra trạng thái và cập nhật UI

Gọi `GET /api/payments/orders/{orderCode}` khoảng **mỗi 5 giây** khi màn hình thanh toán đang mở và đơn còn Pending. Dùng timeout tiếp nối sau khi request trước kết thúc; không tạo nhiều request chồng nhau. Dừng khi rời màn hình hoặc trạng thái thay đổi. Khi tab được focus lại có thể kiểm tra một lần.

| `status` — phân biệt hoa/thường | UI |
|---|---|
| `Pending` | QR, số tiền, nội dung chuyển khoản, countdown từ expiresAt |
| `Paid` | Thông báo thành công, tải lại `/api/auth/me` và `/api/subscriptions/me` |
| `Expired` | Hết thời gian; dừng QR/countdown và cho tạo lần mua mới |
| `Cancelled` | Đơn đã hủy |
| `Failed` | Giao dịch thất bại, hướng dẫn theo thông báo server |
| `RequiresReview` | Chờ đối soát; hiển thị reviewReason khi có, chưa mở quyền PRO |
| `Refunded` | Trạng thái hoàn tiền có trong model; không tự bật PRO |

Countdown về 0 chỉ là trạng thái hiển thị; hỏi lại server để biết trạng thái cuối. Không tự đổi đơn thành Paid hoặc tự cộng ngày PRO. Bấm “Tôi đã chuyển khoản” chỉ nên gọi GET kiểm tra lại đơn.

Các API còn lại:

- `POST /api/payments/orders/{orderCode}/cancel`: hủy đơn Pending còn hạn, không cần body.
- `GET /api/payments/history`: array tối đa 100 đơn gần nhất của mình.
- `GET /api/subscriptions/me`: `tierLevel`, `tierName`, `vipUntil`, `isVip`, `subscriptions`; `isVip` là tên field cũ cho quyền trả phí.
- `GET /api/subscriptions/me/entitlements`: có `accessLevel`, `canAccessInteractive`; các field công cụ học tập cũ không phải tính năng FE mới cần dựng.

Giá và thời hạn luôn lấy từ API. Sau khi mua, JWT hiện tại vẫn dùng được; quyền PRO được BE tính từ DB. Nếu me chưa đổi do cập nhật trạng thái chậm, kiểm tra lại có giới hạn, không sửa store thành PRO chỉ vì người dùng xác nhận đã chuyển tiền.

**FE không gọi** `POST /api/payments/webhooks/sepay`. Đây là endpoint để SePay gọi BE; webhook secret không nằm trong bundle/config FE.

## 7. Player kịch tương tác PRO

### 7.1. Tải cấu hình và tài nguyên

```javascript
const interactive = await api(`/api/dramas/${dramaId}/interactive`);
const assetsById = new Map(interactive.assets.map(asset => [asset.id, asset]));

switch (interactive.manifest.schemaVersion) {
  case 1:
    // Dựng player theo scenes/startSceneId bên dưới.
    break;
  case 0:
    // Chuyển cho renderer legacy dùng interactive.manifest.legacy.
    break;
  default:
    throw new Error("Phiên bản cấu hình chưa được hỗ trợ");
}
```

API trả 401 nếu chưa xác thực, 403 nếu chưa có PRO còn hạn, 404 nếu nội dung chưa được xuất bản/đã tắt hoặc chưa có bản tương tác. Admin có thể dùng `?preview=true` để tải draft; Customer không được dùng preview.

Với schema 1, response gồm `manifest`, `assets`, `publishedAt`, `revision`, `isPreview`. `assets` chỉ gồm các asset được manifest tham chiếu. Một asset có `id`, `kind`, `title`, `url`, `originalFileName`, `contentType`, `sizeBytes`, `isPreview`, `nameVi`, `nameEn`, `meaningVi`, `meaningEn`, `durationSeconds`; metadata không áp dụng có thể null.

URL asset là endpoint có bảo vệ, không phải file public. `<img src>` và `<audio src>` không tự gửi Bearer. Với ảnh/audio có kích thước phù hợp, tải có xác thực rồi tạo object URL:

```javascript
const mediaObjectUrls = new Set();

async function loadAssetObjectUrl(asset) {
  const token = getAccessToken();
  if (!token) throw new ApiError(401, { message: "Cần đăng nhập" });
  const response = await fetch(apiUrl(asset.url), {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store"
  });
  if (!response.ok) {
    if (response.status === 401) setAccessToken(null);
    throw new ApiError(response.status, { message: "Không tải được tài nguyên" });
  }
  const url = URL.createObjectURL(await response.blob());
  mediaObjectUrls.add(url);
  return url;
}

function releasePlayerMedia() {
  for (const url of mediaObjectUrls) URL.revokeObjectURL(url);
  mediaObjectUrls.clear();
}
```

Tải theo cảnh cần dùng, cache trong phiên xem và giải phóng khi thay nội dung/rời player/logout. Với asset lớn cần cơ chế player/stream có header phù hợp; không tải toàn bộ thư viện hoặc video lớn thành Blob. Khi request mới trả 403, dừng player và yêu cầu kiểm tra/nâng cấp subscription; dữ liệu đã tải về thiết bị không thể được thu hồi tức thì bằng URL API.

### 7.2. Manifest schema 1

Manifest tối thiểu có cấu trúc như sau; asset ID phải thay bằng ID upload của chính drama:

```json
{
  "schemaVersion": 1,
  "startSceneId": "scene-01",
  "scenes": [
    {
      "id": "scene-01",
      "backgroundAssetId": 10,
      "durationMs": 15000,
      "nextSceneId": "scene-02",
      "characters": [{ "assetId": 11, "position": { "x": 0.3, "y": 0.5 }, "scale": 1 }],
      "objects": [],
      "audioTracks": [{ "assetId": 12, "startMs": 0, "endMs": 15000 }],
      "hotspots": [
        { "id": "meaning-01", "assetId": 11, "actionType": "SHOW_MEANING", "position": { "x": 0.3, "y": 0.5 } },
        { "id": "next-01", "assetId": 11, "actionType": "GO_TO_SCENE", "targetSceneId": "scene-02" }
      ]
    },
    {
      "id": "scene-02",
      "backgroundAssetId": 10,
      "durationMs": 10000,
      "characters": [], "objects": [], "audioTracks": [], "hotspots": []
    }
  ]
}
```

| Thành phần | Quy tắc và cách FE dựng |
|---|---|
| `startSceneId` | Tìm scene có id tương ứng khi bắt đầu |
| `durationMs`, `startMs`, `endMs` | Đơn vị mili giây; timeline của track/hotspot tương đối với đầu cảnh |
| `position.x/y` | Từ 0 đến 1, đổi sang phần trăm bằng cách nhân 100 |
| `scale` | Lớn hơn 0, tối đa 10; FE áp dụng scale theo renderer của mình |
| `characters` / `objects` | Resolve assetId qua assetsById rồi dựng hình/vị trí |
| `audioTracks` | Lên lịch audio theo startMs/endMs; asset phải là Audio/Music/Effect có MIME audio |
| `nextSceneId` | Đích chuyển cảnh tùy chọn, nếu có phải tồn tại |
| `SHOW_MEANING` | Asset Character/Object; mở popup nameVi/nameEn/meaningVi/meaningEn |
| `GO_TO_SCENE` | Asset Character/Object, targetSceneId hợp lệ; FE chuyển scene |
| `PLAY_AUDIO` | Asset âm thanh Audio/Music/Effect; FE phát audio |

BE lưu và kiểm tra cấu hình; FE quyết định bố cục, điểm neo của hình, cách scale, animation và điều khiển player. Schema hiện không có field riêng cho CSS/animation/rotation/zIndex, volume/loop hoặc audio source offset. Không gửi thêm các field đó vào JSON schema 1: BE từ chối field không được định nghĩa. Nếu editor cần lưu thêm dữ liệu, thống nhất mở rộng schema với BE trước.

Giới hạn: JSON manifest tối đa 1 MiB; 1–500 scene; tối đa 200 item mỗi nhóm characters/objects/audioTracks/hotspots trên một scene; duration cảnh >0 đến một ngày. ID cảnh duy nhất trong manifest, ID hotspot duy nhất trong cảnh. Hotspot có thể có startMs/endMs tùy chọn, nằm trong cảnh. SHOW_MEANING cần asset có nameVi và meaningVi. Không dùng cover làm background của manifest; upload một Background riêng.

### 7.3. Dữ liệu legacy schema 0

Schema 0 trả `manifest.legacy` dạng DramaDto, có `scenes`, `assets`. Scene dùng `sceneKey`, `startMs`, `endMs`, `audioOffsetMs`, background/audio/video và hotspot lồng nhau. Hotspot có `actionKey`, `targetSegmentIds`, giải nghĩa Việt–Anh và vùng nhấn x/y/width/height theo **0–100%**.

Không áp dụng quy tắc tọa độ 0–1 của schema 1 lên schema 0. Giữ renderer legacy cho nội dung chưa chuyển đổi; không tự coi schema 0 là schema 1 hoặc tự bỏ ActionKey/TargetSegmentIds.

## 8. Admin Studio: tạo và xuất bản kịch

Tất cả route `/api/admin/...` cần Bearer của user `Admin`. Đặt guard FE theo role để hiển thị menu, nhưng quyền cuối cùng vẫn do BE kiểm tra.

### 8.1. Tạo draft và upload video FREE

```javascript
const form = new FormData();
form.append("title", "Tên vở kịch");
form.append("description", "Mô tả");
if (selectedWorkId) form.append("literaryWorkId", String(selectedWorkId));
if (coverFile) form.append("coverImage", coverFile);

const draft = await api("/api/admin/dramas", { method: "POST", body: form });
const videoForm = new FormData();
videoForm.append("file", videoFile);
const videoAsset = await api(`/api/admin/dramas/${draft.id}/video`, {
  method: "POST", body: videoForm
});

await api(`/api/admin/dramas/${draft.id}/status`, {
  method: "PATCH", json: { status: "PUBLISHED" }
});
```

Tạo draft cũng nhận JSON `{ "title": "...", "description": "...", "literaryWorkId": null }`. Response hiện là `200` + DramaDto, dùng `draft.id`; không chờ một wrapper data hoặc chỉ chấp nhận 201. Không gửi `isPublished: true` khi tạo. Video-only không cần tạo chapter/scene/timeline/hotspot.

### 8.2. Upload asset PRO trên một endpoint chung

Upload cùng loại theo batch, tối đa 20 file/request:

```javascript
const form = new FormData();
form.append("assetType", "CHARACTER");
for (const file of selectedFiles) form.append("files", file);
const uploadedAssets = await api(`/api/admin/dramas/${dramaId}/assets`, {
  method: "POST", body: form
}); // Array DramaAssetDto; lưu id để gắn vào manifest.
```

AssetType nhận `Background`, `Character`, `Object`, `Audio`, `Music`, `Effect`, không phân biệt hoa/thường. Mỗi request batch dùng một loại; nếu người dùng kéo nhiều loại cùng lúc, FE chia thành các batch. Upload video FREE dùng route `/video`.

Form một file tương thích cũ dùng `kind`, `title`, `file`, trả **một object asset**, không phải array. Không gửi đồng thời `file` và `files`. Batch tự lấy title từ tên file; cần tên tùy ý có thể dùng form một file.

| Nhóm | File được hỗ trợ | Giới hạn/file |
|---|---|---|
| Background/Character/Object | JPG/JPEG, PNG, WebP, GIF (`image/gif`) | 10 MiB |
| Audio/Music | MP3, WAV, M4A, OGG | 30 MiB |
| Effect | Ảnh hoặc audio trong các định dạng trên | Theo nhóm ảnh/audio |
| FREE video | MP4, WebM | 200 MiB |
| Cover | JPG/JPEG, PNG, WebP | 5 MiB |

Nhân vật/đồ vật có thể upload `.gif` với MIME `image/gif` qua form một file hoặc batch. BE chấp nhận header `GIF87a`/`GIF89a` theo [đặc tả GIF](https://www.w3.org/Graphics/GIF/spec-gif89a.txt), lưu nguyên file và API media trả `Content-Type: image/gif`, giữ các frame/chuyển động. Khi FE tải media có Bearer theo mục 7, dùng Blob URL của file GIF cho thẻ `<img>` để hiển thị ảnh động.

Toàn request upload tối đa khoảng 202 MiB. BE kiểm tra MIME, extension và chữ ký file; `accept` ở input FE chỉ hỗ trợ trải nghiệm chọn file. Batch lỗi sẽ rollback cả batch; FE không giữ các ID từ một request thất bại.

### 8.3. Metadata, lưu draft, preview và publish

Nhân vật/đồ vật dùng SHOW_MEANING cần metadata. API PUT metadata thay toàn bộ các field sau; gửi đủ dữ liệu muốn giữ, vì field bỏ qua có thể bị đặt null:

```javascript
await api(`/api/admin/dramas/${dramaId}/assets/${assetId}/metadata`, {
  method: "PUT",
  json: {
    nameVi: "Tên nhân vật", nameEn: "Character name",
    meaningVi: "Giải nghĩa", meaningEn: "Meaning",
    durationSeconds: null
  }
});
```

Name tối đa 200 ký tự; meaning tối đa 8000. durationSeconds nếu có phải >0 đến 86400; audio track không được dài hơn duration đã khai báo.

Luồng lưu editor:

```javascript
const editing = await api(`/api/admin/dramas/${dramaId}/manifest`);
// editing = { manifest, revision, publishedAt, legacy }
// manifest có thể null khi chưa lưu schema 1.

const saved = await api(`/api/admin/dramas/${dramaId}/manifest`, {
  method: "PUT",
  json: { revision: editing.revision, manifest: editorManifest }
});
// saved = { revision, updatedAt }; cập nhật revision trong editor store.

const preview = await api(`/api/dramas/${dramaId}/interactive?preview=true`);
```

Nếu 409 khi lưu, giữ bản đang sửa trên FE, tải lại revision/draft và cho người dùng đối chiếu trước khi lưu tiếp; không ghi đè mù bằng revision mới. Publish cũng tăng revision, vì vậy sau publish phải lấy lại GET manifest hoặc dùng revision trả về từ API status.

| Trạng thái gửi qua PATCH status | Hiệu quả |
|---|---|
| `DRAFT` | Chưa công khai với Customer |
| `PUBLISHED` | Xuất bản video và/hoặc cấu hình đã chuẩn bị |
| `DISABLED` | Ẩn và chặn Customer; Admin vẫn quản lý/preview được |

Sửa draft manifest không đổi bản Customer đang xem. Sửa thông tin cơ bản, thay video hoặc cover có thể có hiệu lực ngay trên tác phẩm đã published; không coi toàn bộ metadata/media là một transaction draft. Asset đang được bản xuất bản tham chiếu không được sửa metadata/xóa: upload asset mới → đổi tham chiếu trong draft → publish lại → xóa asset cũ khi không còn dùng.

### 8.4. Những API Admin Studio còn cần

| Method | Route | Response/lưu ý |
|---|---|---|
| GET | `/api/admin/dramas` | Array, gồm draft/published/disabled; chưa phân trang |
| GET | `/api/admin/dramas/{id}` | DramaDto đầy đủ cho editor; assets/scenes là cấu trúc legacy, manifest v1 lấy riêng |
| PUT | `/api/admin/dramas/{id}` | JSON metadata như khi tạo; gửi đủ title/description/literaryWorkId cần giữ, bỏ isPublished |
| GET | `/api/admin/dramas/{id}/assets` | Array metadata; URL preview dạng `/api/admin/dramas/.../media` |
| POST | `/api/admin/dramas/{id}/cover` | FormData field file; trả asset |
| DELETE | `/api/admin/dramas/{id}/assets/{assetId}` | 204; 409 nếu asset còn được tham chiếu |
| DELETE | `/api/admin/dramas/{id}` | Chỉ draft rỗng chưa từng xuất bản/chưa có lịch sử; còn dữ liệu trả 409 |
| GET | `/api/admin/works?page=1&pageSize=20&search=...` | Metadata tác phẩm văn học để chọn literaryWorkId, trả items/page/pageSize/totalItems/totalPages |

Với tác phẩm đã có dữ liệu nghiệp vụ, dùng DISABLED để ngừng cung cấp, không coi DELETE là thao tác tắt tác phẩm.

## 9. Các màn hình Admin khác

| Màn hình | API và bộ lọc |
|---|---|
| Dashboard | `GET /api/admin/dashboard?fromUtc=...&toUtc=...&groupBy=day` (`day` hoặc `month`) |
| Người dùng | `GET /api/admin/users?search=...&accessLevel=PRO&locked=false&page=1&pageSize=20` |
| Chi tiết user | `GET /api/admin/users/{id}`; **response vẫn là object phân trang, lấy items[0]** |
| Khóa/mở khóa | `PATCH /api/admin/users/{id}/lock`, body `{ "isLocked": true }` hoặc false |
| Giao dịch | `GET /api/admin/payments?status=Paid&orderCode=...&userId=8&fromUtc=...&toUtc=...`; array, tối đa 200 đơn |
| Chi tiết giao dịch | `GET /api/admin/payments/{id}`; id số, khác orderCode của Customer; trả order/transactions/audits |
| Đối soát | `GET /api/admin/payments/reconciliation`; trả items/count/message, không có nút tự xác nhận Paid |
| Quản lý đánh giá | `GET /api/admin/reviews?dramaId=12&status=Published&search=...&page=1&pageSize=20` |
| Ẩn/hiện đánh giá | `PATCH /api/admin/reviews/{reviewId}/status`, body `{ "status": "Hidden" }` hoặc Published |
| Xem gói dịch vụ | `GET /api/admin/subscription-packages`; array cả active/inactive |

User filter `accessLevel` chỉ nhận FREE/PRO, status feedback dùng Published/Hidden, status drama dùng DRAFT/PUBLISHED/DISABLED. Giữ đúng casing từng nhóm.

Dashboard trả `totalRevenue`, `todayRevenue`, `monthRevenue`, `rangeRevenue`, `periods`, `successfulTransactions`, `pendingTransactions`, `totalUsers`, `proUsers`, `freeUsers`, `totalDramas`, `publishedDramas`, `disabledDramas`, `averageRating`. Một period có year/month/day/revenue/transactions. Khoảng dashboard là `[fromUtc,toUtc)`, nhóm ngày/tháng theo UTC+7; gửi thời gian UTC với hậu tố Z. Bộ lọc admin/payments dùng thời điểm tạo đơn, dashboard dùng thời điểm PaidAt.

Các thời gian thanh toán/subscription có offset/Z dùng trực tiếp để tính countdown/format. Một số timestamp metadata cũ có thể chưa có offset trong JSON; thống nhất múi giờ với BE trước khi diễn giải, không tự coi mọi chuỗi không offset là giờ UTC.

API quản lý gói hiện có POST `/api/admin/subscription-packages` và PUT `/api/admin/subscription-packages/{id}`; body gồm name/description/benefits/price/durationInDays/tierLevel/isActive. Chỉ dựng form này nếu màn hình có nhu cầu quản lý gói. FE không tự tính lại hay thay giá gói đã có; gói chuẩn có thể bị từ chối đổi tier/thời hạn.

## 10. Xử lý lỗi và các route cần tránh

| HTTP | FE nên xử lý |
|---|---|
| 200 / 201 | Parse response theo kiểu của endpoint, không mặc định có data |
| 204 | Thành công không body; không gọi response.json() |
| 400 | Hiển thị message hoặc errors của validation; sửa form/cấu hình |
| 401 | Login sai hoặc phiên không hợp lệ; nếu đang dùng token cũ thì xóa phiên, về đăng nhập |
| 403 | Giữ phiên; kiểm tra role/PRO hoặc thao tác chỉ dành Admin |
| 404 | Nội dung không tồn tại/chưa công khai/đã tắt/không thuộc phạm vi được xem |
| 409 | Trùng email, chưa đủ điều kiện feedback, revision lệch hoặc asset đang dùng; tải lại trạng thái phù hợp |
| 410 | Chức năng đã ngừng, chuyển sang endpoint mới |
| 413 / 415 | File quá lớn / định dạng-MIME-signature không được hỗ trợ |
| 429 | Tạm khóa nút, chờ rồi mới thử lại; không tự retry liên tục |
| 500 / 503 | Hiển thị lỗi dịch vụ, lưu traceId nếu có để BE kiểm tra |

Lỗi có thể là `{ "message": "...", "traceId": "..." }`, `{ "code": "...", "message": "..." }`, hoặc ProblemDetails có `title`, `errors`. Một số 401/403/404 không có body. Không phụ thuộc vào một chuỗi message tiếng Việt cố định để chạy nghiệp vụ; dùng status và code khi có.

Nếu API trả `503` với `code: "DATABASE_UPGRADE_REQUIRED"`, BE đang thiếu cột/bảng tương thích và cần kiểm tra migration. Lỗi này có thể xuất hiện cả khi login hoặc tạo kịch/upload bìa. `DATABASE_UNAVAILABLE` là lỗi kết nối/cấu hình SQL Server. Với hai lỗi này, hiển thị thông báo dịch vụ và gửi traceId cho BE; đổi mật khẩu hoặc lặp request không giải quyết được. Với tạo kịch, BE có script chỉ đọc `scripts/Diagnose_Drama_Schema.sql` để tìm cột/bảng thiếu.

Nếu upload trả `400` và `errors[""]` chứa `MinRequestBodyDataRate` / `data arriving too slowly`, request bị ngắt khi đang truyền file. Hiển thị lỗi kết nối upload, không gắn lỗi này vào trường Title/Description. BE đã nới ngưỡng cho các route multipart HTTP/1.x qua proxy; cần khởi động lại Backend sau khi cập nhật code. Khi kiểm tra, thử cùng file bằng URL localhost rồi bằng URL ngrok và gửi traceId cho BE nếu vẫn lỗi. Giới hạn file bìa vẫn là 5 MiB. Không tự retry POST tạo kịch liên tục; cho người dùng chủ động thử lại.

Giới hạn hiện tại: auth nhạy cảm 10 request/phút/IP; payment 20 request/phút/tài khoản; upload 6 request/phút/tài khoản; ghi feedback 10 request/phút/tài khoản. Upload batch giúp tránh gọi một request cho từng file. API không cam kết luôn trả Retry-After.

| Route cũ hoặc dễ đoán nhầm | Dùng gì thay thế |
|---|---|
| `/api/works/{id}/content`, reviews của works | Đã trả 410; dùng drama video/interactive/feedback |
| Admin `/preview`, `/preview-media` | Đã trả 410; preview bằng interactive?preview=true hoặc loader video có Bearer |
| Scene/hotspot CRUD và customer scene/hotspot | Adapter legacy deprecated; FE mới dùng manifest |
| `POST /api/dramas/{id}/reviews`, `PUT/DELETE /api/reviews/{reviewId}` | Adapter deprecated; dùng PUT/DELETE feedback theo dramaId |
| `PATCH /api/admin/dramas/{id}/publish` | Adapter deprecated; dùng PATCH status |
| `/api/dramas/{id}/feedbacks` | Không có route này; list dùng `/reviews` |
| `/api/subscriptions/packages` | Không có; dùng `/api/subscription-packages` |
| `POST /api/payments`, `/api/payments/me` | Không có; dùng `/api/payments/orders`, `/api/payments/history` |
| `/api/auth/refresh-token` | Chưa có trong kiến trúc hiện tại |

## 11. Checklist bàn giao FE

- [ ] API base, CORS và cookie Google hoạt động ở môi trường FE thực tế.
- [ ] Đăng ký không được xem là login; đã có màn xác minh/gửi lại/reset mật khẩu.
- [ ] Lấy quyền bằng me; Admin không bị chặn chỉ vì accessLevel FREE.
- [ ] Danh sách kịch xử lý array; không dùng sceneCount để quyết định có video.
- [ ] FREE video phát/seek bằng Range; user đăng nhập có request media Bearer để đánh giá.
- [ ] PRO loader có Bearer, hiểu schema 0/1 và giải phóng object URL.
- [ ] FormData đúng tên field, không tự đặt multipart Content-Type.
- [ ] Editor giữ revision mới sau save/publish, xử lý 409, không đổi bản published khi lưu draft.
- [ ] Thanh toán giữ idempotency key khi retry, hiển thị bankAccount và chỉ mở PRO sau xác nhận server.
- [ ] Polling dừng đúng lúc; feedback không gửi userId/isWatched.
- [ ] 401/403/404/409/410/413/415/429 có UI phù hợp; không lặp retry vô hạn.

Tài liệu mô tả contract của source hiện tại; việc gọi được luồng SQL Server/SMTP/Google/SePay thật còn phụ thuộc migration và cấu hình môi trường BE. Không cần FE sửa database hoặc giữ secret của BE để tích hợp.
