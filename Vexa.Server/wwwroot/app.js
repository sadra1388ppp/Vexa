const state = { token: localStorage.getItem('vexa_token'), me: JSON.parse(localStorage.getItem('vexa_user') || 'null'), chats: [], online: new Set(), activeChat: null, connection: null };

const $ = id => document.getElementById(id);
const authView = $('authView');
const chatView = $('chatView');
const authStatus = $('authStatus');

function toast(message){const el=$('toast');el.textContent=message;el.classList.add('show');clearTimeout(window.__toast);window.__toast=setTimeout(()=>el.classList.remove('show'),2200)}
function setAuthStatus(message=''){authStatus.textContent=message}
function apiUrl(path){return path}
async function api(path, options={}){
  const headers = new Headers(options.headers || {});
  if(state.token) headers.set('Authorization', `Bearer ${state.token}`);
  if(options.body && !(options.body instanceof FormData)) headers.set('Content-Type','application/json');
  const response = await fetch(apiUrl(path), {...options, headers});
  let data = null;
  try { data = await response.json(); } catch {}
  if(!response.ok){ throw new Error(data?.message || data?.title || `Request failed (${response.status})`); }
  return data;
}

function showAuth(){authView.classList.remove('hidden');chatView.classList.add('hidden')}
function showChat(){authView.classList.add('hidden');chatView.classList.remove('hidden');$('meLabel').textContent=state.me?`${state.me.displayName || state.me.id} • ${state.me.id}`:''}
function chatOtherUser(chat){return String(chat.user1Id).toLowerCase()===String(state.me.id).toLowerCase()?{id:chat.user2Id,name:chat.user2Name,avatar:chat.user2AvatarUrl}:{id:chat.user1Id,name:chat.user1Name,avatar:chat.user1AvatarUrl}}
function messageId(m){return m?.id ?? m?.Id}
function messageChatId(m){return m?.chatId ?? m?.ChatId}
function messageSenderId(m){return m?.senderId ?? m?.SenderId}
function messageContent(m){return m?.content ?? m?.Content ?? ''}
function messageTime(m){return m?.sentAt ?? m?.SentAt}
function renderAvatar(name){return (name || '?').trim().charAt(0).toUpperCase() || '?'}
function renderChats(){
  const box=$('chatsList');box.innerHTML='';
  if(!state.chats.length){box.innerHTML='<div class="empty">No chats yet.<br>Search a User ID to start a conversation.</div>';return}
  [...state.chats].sort((a,b)=>new Date(b.lastMessage?.sentAt||b.createdAt)-new Date(a.lastMessage?.sentAt||a.createdAt)).forEach(chat=>{
    const other=chatOtherUser(chat); const last=chat.lastMessage;
    const item=document.createElement('button'); item.className='chat-item';
    item.innerHTML=`<div class="avatar">${renderAvatar(other.name||other.id)}</div><div class="chat-meta"><div class="chat-name">${escapeHtml(other.name||other.id)}</div><div class="chat-preview">${escapeHtml(last?messageContent(last):'Start chatting')}</div></div>`;
    item.onclick=()=>openChat(chat);box.appendChild(item);
  });
}
function escapeHtml(value){return String(value??'').replace(/[&<>'"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[c]))}
function formatTime(value){if(!value)return'';const d=new Date(value);if(Number.isNaN(d.getTime()))return'';return d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})}

async function connectSignalR(){
  if(!state.token || !window.signalR) return;
  if(state.connection){try{await state.connection.stop()}catch{}}
  state.connection = new signalR.HubConnectionBuilder().withUrl('/hubs/chat',{accessTokenFactory:()=>state.token}).withAutomaticReconnect().build();
  state.connection.on('PresenceSnapshot', users=>{state.online=new Set((users||[]).map(String));updateConversationStatus()});
  state.connection.on('UserOnline', id=>{state.online.add(String(id));updateConversationStatus()});
  state.connection.on('UserOffline', id=>{state.online.delete(String(id));updateConversationStatus()});
  state.connection.on('ReceiveMessage', message=>{
    if(state.activeChat && String(messageChatId(message))===String(state.activeChat.id)) renderMessage(message);
    const chat=state.chats.find(c=>String(c.id)===String(messageChatId(message)));
    if(chat){chat.lastMessage=message;renderChats()}
  });
  state.connection.on('ChatCreated', async ()=>{try{await loadChats()}catch{}});
  state.connection.on('ChatDeleted', async payload=>{state.chats=state.chats.filter(c=>String(c.id)!==String(payload?.chatId));renderChats();if(state.activeChat&&String(state.activeChat.id)===String(payload?.chatId))closeChat()});
  state.connection.on('MessageDeleted', payload=>{
    const node=document.querySelector(`[data-message-id="${CSS.escape(String(payload?.id))}"]`);if(node)node.remove();
  });
  state.connection.on('ProfileUpdated', async payload=>{try{await loadChats()}catch{}});
  try{await state.connection.start();}catch(e){toast('Real-time connection failed')}
}
function updateConversationStatus(){
  if(!state.activeChat)return; const id=chatOtherUser(state.activeChat).id;
  $('conversationStatus').textContent=state.online.has(String(id))?'Online':'Offline';
}

async function loadChats(){state.chats=await api('/api/Chat');renderChats()}
async function openChat(chat){
  state.activeChat=chat;$('chatListView').classList.add('hidden');$('conversationView').classList.remove('hidden');
  const other=chatOtherUser(chat);$('conversationName').textContent=other.name||other.id;updateConversationStatus();
  $('messages').innerHTML='<div class="empty">Loading…</div>';
  try{
    const history=await api(`/api/Chat/${chat.id}/messages?pageSize=50`);$('messages').innerHTML='';(history.messages||[]).slice().reverse().forEach(renderMessage);
    $('messages').scrollTop=$('messages').scrollHeight;
    if(state.connection) await state.connection.invoke('JoinChat',chat.id).catch(()=>{});
  }catch(e){$('messages').innerHTML=`<div class="empty">${escapeHtml(e.message)}</div>`}
}
function renderMessage(message){
  const id=messageId(message);const content=messageContent(message); if(id==null)return;
  const existing=document.querySelector(`[data-message-id="${CSS.escape(String(id))}"]`);if(existing)return;
  const mine=String(messageSenderId(message)).toLowerCase()===String(state.me.id).toLowerCase();
  const node=document.createElement('div');node.className=`bubble ${mine?'mine':'theirs'}`;node.dataset.messageId=String(id);
  node.innerHTML=`<div>${escapeHtml(content)}</div><div class="time">${escapeHtml(formatTime(messageTime(message)))}</div>`;
  $('messages').appendChild(node);$('messages').scrollTop=$('messages').scrollHeight;
}
async function closeChat(){if(state.connection&&state.activeChat)await state.connection.invoke('LeaveChat',state.activeChat.id).catch(()=>{});state.activeChat=null;$('conversationView').classList.add('hidden');$('chatListView').classList.remove('hidden')}

async function searchUser(){
  const q=$('userSearch').value.trim();if(!q){$('searchResults').classList.add('hidden');return}
  try{const users=await api(`/api/User/search?q=${encodeURIComponent(q)}`);const box=$('searchResults');box.innerHTML='';box.classList.remove('hidden');
    if(!users.length){box.innerHTML='<div class="empty">User not found.</div>';return}
    users.slice(0,8).forEach(user=>{const item=document.createElement('button');item.className='search-item';item.innerHTML=`<div class="avatar">${renderAvatar(user.displayName||user.id)}</div><div class="chat-meta"><div class="chat-name">${escapeHtml(user.displayName||user.id)}</div><div class="chat-preview">@${escapeHtml(user.id)}</div></div>`;item.onclick=()=>createChat(user.id);box.appendChild(item)})
  }catch(e){toast(e.message)}
}
async function createChat(userId){
  try{const result=await api('/api/Chat',{method:'POST',body:JSON.stringify({userId})});$('searchResults').classList.add('hidden');$('userSearch').value='';await loadChats();const chat=state.chats.find(c=>String(c.id)===String(result?.chat?.id));if(chat)openChat(chat);else toast('Chat created. Refreshing…')}catch(e){toast(e.message)}
}

$('loginTab').onclick=()=>{ $('loginTab').classList.add('active');$('registerTab').classList.remove('active');$('loginForm').classList.remove('hidden');$('registerForm').classList.add('hidden');setAuthStatus('') };
$('registerTab').onclick=()=>{ $('registerTab').classList.add('active');$('loginTab').classList.remove('active');$('registerForm').classList.remove('hidden');$('loginForm').classList.add('hidden');setAuthStatus('') };
$('loginForm').onsubmit=async e=>{e.preventDefault();setAuthStatus('Signing in…');try{const data=await api('/api/User/login',{method:'POST',body:JSON.stringify({id:$('loginId').value.trim(),password:$('loginPassword').value})});state.token=data.token;state.me=data.user;localStorage.setItem('vexa_token',state.token);localStorage.setItem('vexa_user',JSON.stringify(state.me));showChat();await loadChats();await connectSignalR();setAuthStatus('')}catch(err){setAuthStatus(err.message)}};
$('registerForm').onsubmit=async e=>{e.preventDefault();setAuthStatus('Creating account…');try{await api('/api/User/register',{method:'POST',body:JSON.stringify({id:$('registerId').value.trim(),displayName:$('registerName').value.trim(),email:$('registerEmail').value.trim(),phoneNumber:$('registerPhone').value.trim(),password:$('registerPassword').value})});toast('Account created');$('loginTab').click();$('loginId').value=$('registerId').value.trim();$('loginPassword').focus()}catch(err){setAuthStatus(err.message)}};
$('logoutButton').onclick=async()=>{try{await state.connection?.stop()}catch{}localStorage.removeItem('vexa_token');localStorage.removeItem('vexa_user');state.token=null;state.me=null;state.chats=[];state.activeChat=null;showAuth()};
$('searchButton').onclick=searchUser;$('userSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();searchUser()}});$('backButton').onclick=closeChat;
$('messageForm').onsubmit=async e=>{e.preventDefault();const content=$('messageInput').value.trim();if(!content||!state.activeChat)return;$('messageInput').value='';try{if(state.connection?.state===signalR.HubConnectionState.Connected)await state.connection.invoke('SendMessage',state.activeChat.id,content);else{const result=await api(`/api/Chat/${state.activeChat.id}/messages`,{method:'POST',body:JSON.stringify({content})});renderMessage(result.data)}}catch(err){toast(err.message)}};

window.addEventListener('load',async()=>{if(state.token&&state.me){showChat();try{await loadChats();await connectSignalR()}catch(e){toast(e.message)}}else showAuth()});
if('serviceWorker' in navigator && location.protocol==='https:'){navigator.serviceWorker.register('/sw.js').catch(()=>{})}
