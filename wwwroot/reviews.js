// Shared review widget. targetType is "works" or "dramas"; all user text uses textContent.
window.mountReviews = function (host, targetType, targetId, token) {
  let page = 1, editing = null, busy = false, generation = 0;
  const base = `/api/${targetType}/${encodeURIComponent(targetId)}/reviews`;
  host.replaceChildren();
  const node = (tag, text) => { const el = document.createElement(tag); if (text !== undefined) el.textContent = text; return el; };
  const heading = node('h2', 'Đánh giá & Bình luận'), summary = node('p', 'Đang tải...'), message = node('p'), list = node('div');
  const form = node('form'), rating = node('select'), comment = node('textarea'), submit = node('button', 'Gửi đánh giá'), cancel = node('button', 'Hủy sửa');
  const prev = node('button', 'Trang trước'), next = node('button', 'Trang sau'), pagination = node('span');
  for (let n = 1; n <= 5; n++) rating.add(new Option('★'.repeat(n) + '☆'.repeat(5-n), n));
  rating.value = '5'; rating.setAttribute('aria-label', 'Số sao');
  comment.maxLength = 2000; comment.required = true; comment.placeholder = 'Viết bình luận'; comment.setAttribute('aria-label', 'Bình luận');
  comment.style.cssText = 'display:block;width:100%;min-height:80px;margin:8px 0';
  submit.type = 'submit'; cancel.type = prev.type = next.type = 'button'; cancel.hidden = true;
  form.append(rating, comment, submit, cancel); host.append(heading, summary, message, list, prev, pagination, next, form);
  const describe = status => ({401:'Vui lòng đăng nhập để đánh giá.',403:'Bạn không có quyền thực hiện thao tác này.',404:'Không tìm thấy nội dung hoặc đánh giá.',409:'Bạn đã đánh giá nội dung này.',429:'Bạn thao tác quá nhanh. Vui lòng thử lại sau.'}[status] || `Không thể hoàn tất yêu cầu (${status}).`);
  async function request(url, options = {}) {
    const res = await fetch(url, {...options, headers:{...(token ? {Authorization:'Bearer '+token} : {}), ...(options.body ? {'Content-Type':'application/json'} : {})}});
    if (!res.ok) { const body = await res.json().catch(()=>null); const validation = body?.errors && Object.values(body.errors).flat().join(' '); throw Error(body?.message || validation || describe(res.status)); }
    return res.status === 204 ? null : res.json();
  }
  function reset() { editing = null; comment.value = ''; rating.value = '5'; submit.textContent = 'Gửi đánh giá'; cancel.hidden = true; }
  async function load() {
    const current = ++generation; message.textContent = 'Đang tải đánh giá...'; prev.disabled = next.disabled = true;
    try {
      const [stats, data] = await Promise.all([request(base+'/summary'), request(base+'?page='+page+'&pageSize=10')]);
      if (current !== generation) return;
      summary.textContent = `★ ${stats.averageRating.toFixed(1)} / 5 · ${stats.totalReviews} lượt đánh giá`;
      list.replaceChildren();
      if (!data.items.length) list.append(node('p', 'Chưa có đánh giá.'));
      for (const r of data.items) {
        const item = node('article'), title = node('strong', `${r.displayName} · ${'★'.repeat(r.rating)}${'☆'.repeat(5-r.rating)}`);
        const date = node('small', ' · '+new Date(r.createdAt+'Z'.repeat(!/[zZ]|[+-]\d\d:\d\d$/.test(r.createdAt))).toLocaleDateString('vi-VN'));
        const text = node('p', r.comment); text.style.whiteSpace = 'pre-wrap'; item.append(title, date, text);
        if (r.isMine) {
          const edit = node('button', 'Sửa'), remove = node('button', 'Xóa'); edit.type = remove.type = 'button';
          edit.onclick = () => { editing=r.id; rating.value=String(r.rating); comment.value=r.comment; submit.textContent='Lưu thay đổi'; cancel.hidden=false; comment.focus(); };
          remove.onclick = async () => { if (busy || !confirm('Xóa đánh giá này?')) return; busy=true; remove.disabled=true; try { await request('/api/reviews/'+r.id,{method:'DELETE'}); reset(); await load(); } catch(e) { message.textContent=e.message; } finally { busy=false; remove.disabled=false; } };
          item.append(edit, remove);
        }
        list.append(item);
      }
      pagination.textContent = ` ${data.page} / ${Math.max(1,data.totalPages)} `; prev.disabled=page<=1; next.disabled=page>=data.totalPages;
      message.textContent = token ? '' : 'Đăng nhập để gửi đánh giá.';
    } catch(e) { if (current === generation) message.textContent = e.message; }
  }
  prev.onclick=()=>{if(page>1){page--;load();}}; next.onclick=()=>{page++;load();}; cancel.onclick=reset;
  form.onsubmit=async e=>{e.preventDefault();if(busy)return;if(!token){message.textContent=describe(401);return;}const text=comment.value.trim();if(!text){message.textContent='Vui lòng nhập bình luận.';return;}
    busy=true;submit.disabled=true;try{await request(editing?'/api/reviews/'+editing:base,{method:editing?'PUT':'POST',body:JSON.stringify({rating:Number(rating.value),comment:text})});reset();page=1;await load();}catch(err){message.textContent=err.message;}finally{busy=false;submit.disabled=false;}};
  load();
};
