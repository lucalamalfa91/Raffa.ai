/**
 * Italian for Workspace e membri (`routes/workspace/members`): the roster, the invite pane, the
 * row actions, the Ask Raffa web-search setting, Guida Raffa and the screen's tips. Shared ids the
 * screen also shows ("Workspace & members", "Status", "You", "Cancel", "Retry", "Ask Raffa",
 * "Expired") already live in the other area files.
 */
export const IT_MEMBERS: Readonly<Record<string, string>> = {
  // Header and screen states
  "Setup": "Configurazione",
  "{name} · tenant {id}": "{name} · tenant {id}",
  "tenant {id}": "tenant {id}",
  "Tip: invite the team once the first contract is validated — there is nothing for them to ask before that.":
    "Suggerimento: invita il team dopo aver validato il primo contratto — prima non c'è nulla su cui possano fare domande.",
  "No workspace selected": "Nessun workspace selezionato",
  "Choose a workspace before managing members.": "Scegli un workspace prima di gestire i membri.",
  "Loading members…": "Caricamento dei membri…",
  "Members unavailable": "Membri non disponibili",
  "The roster could not be loaded.": "Non è stato possibile caricare l'elenco dei membri.",

  // Roles and statuses
  "Workspace Admin": "Admin del workspace",
  "Procurement": "Procurement",
  "Asks, uploads, reviews, triages renewals": "Chiede, carica, verifica, smista i rinnovi",
  "Also deletes documents and manages members": "In più elimina documenti e gestisce i membri",
  "Invited": "Invitato",
  "Active": "Attivo",

  // Members table and row actions
  "Member": "Membro",
  "Role": "Ruolo",
  "Actions": "Azioni",
  "Send a new invitation": "Invia un nuovo invito",
  "Revoke": "Revoca",
  "Remove": "Rimuovi",
  "Yes, revoke": "Sì, revoca",
  "Revoking…": "Revoca in corso…",
  "Yes, send a new invitation": "Sì, invia un nuovo invito",
  "Yes, remove": "Sì, rimuovi",
  "Removing…": "Rimozione in corso…",
  "This is the last Workspace Admin. Invite another Workspace Admin first.":
    "È l'ultimo Admin del workspace. Invita prima un altro Admin del workspace.",
  "Revoke the invitation for {email}?": "Revocare l'invito per {email}?",
  "Their link stops working. They never had access to this workspace.":
    "Il suo link smette di funzionare. Non ha mai avuto accesso a questo workspace.",
  "Send a new invitation to {email}?": "Inviare un nuovo invito a {email}?",
  "The link you already shared stops working.": "Il link che hai già condiviso smette di funzionare.",
  "Remove yourself from this workspace?": "Rimuoverti da questo workspace?",
  "You will lose access immediately and will need a new invitation to return.":
    "Perderai subito l'accesso e per tornare ti servirà un nuovo invito.",
  "Remove {email}?": "Rimuovere {email}?",
  "They lose access immediately. To bring them back you will need to send a new invitation.":
    "Perde subito l'accesso. Per riammettere questa persona dovrai inviare un nuovo invito.",
  "The invitation could not be revoked.": "Non è stato possibile revocare l'invito.",
  "The member could not be removed.": "Non è stato possibile rimuovere il membro.",

  // Invite pane
  "Invite a colleague": "Invita un collega",
  "Work email": "Email di lavoro",
  "name@{domain}": "nome@{domain}",
  "name@company.com": "nome@azienda.com",
  "Send invitation": "Invia l'invito",
  "Sending…": "Invio in corso…",
  "An email is required.": "Serve un indirizzo email.",
  "Enter a valid email address.": "Inserisci un indirizzo email valido.",
  "{email} is outside {domain}. They will get full {role} access to this workspace.":
    "{email} è fuori dal dominio {domain}. Avrà accesso completo come {role} a questo workspace.",
  "The invitation could not be created.": "Non è stato possibile creare l'invito.",
  "Invitation sent to {email}.": "Invito inviato a {email}.",
  "Invitation created, but the email could not be sent.": "Invito creato, ma non è stato possibile inviare l'email.",
  "Invitation ready for {email}.": "Invito pronto per {email}.",
  "Invitation link": "Link dell'invito",
  "Copy link": "Copia il link",
  "Copied⟦link⟧": "Copiato",
  "It expires {date}, can be used once, and is not shown again.":
    "Scade il {date}, si può usare una sola volta e non verrà più mostrato.",
  "They will get a one-time code from Microsoft the first time they sign in.":
    "Al primo accesso riceverà da Microsoft un codice monouso.",
  "No invitation was created.": "Non è stato creato alcun invito.",
  "Raffa.ai is not allowed to add guests to your company directory yet. A tenant administrator has to approve that permission.":
    "Raffa.ai non è ancora autorizzata ad aggiungere ospiti alla directory aziendale. Un amministratore del tenant deve approvare questa autorizzazione.",
  "Your company directory would not add {email}. Check the address, or ask a tenant administrator.":
    "La directory aziendale non ha aggiunto {email}. Controlla l'indirizzo o chiedi a un amministratore del tenant.",
  "Your company directory could not be reached. Try again in a few minutes.":
    "Non è stato possibile raggiungere la directory aziendale. Riprova tra qualche minuto.",
  "Raffa.ai could not create this invitation.": "Raffa.ai non è riuscita a creare questo invito.",

  // Procurement (read-only) variant
  "Request access": "Richiedi l'accesso",
  "You don't manage this workspace": "Non gestisci questo workspace",
  "Only a Workspace Admin can invite, revoke or remove members. Ask one of your workspace's Admins for access.":
    "Solo un Admin del workspace può invitare, revocare o rimuovere membri. Chiedi l'accesso a uno degli Admin del tuo workspace.",
  "Workspace Admin access — {name}": "Accesso come Admin del workspace — {name}",
  "Workspace Admin access": "Accesso come Admin del workspace",

  // Ask Raffa web-search setting
  "Allow Ask Raffa to search the public web": "Consenti a Chiedi a Raffa di cercare sul web pubblico",
  "Off by default. When on, everyone here can switch on Web search in Ask Raffa's composer: questions then go to the public web and to your contracts together, and a personal question is pointed at Google or an AI search tool. With the toggle off, Raffa may still offer a web search for a procurement question and asks permission first. Only the question's words are searched -- nothing from your contracts leaves Raffa; web results are labelled unverified.":
    "Disattivata di default. Se attiva, tutti qui possono accendere la ricerca web nel campo di scrittura di Chiedi a Raffa: le domande vanno allora al web pubblico e ai tuoi contratti insieme, e una domanda personale viene indirizzata a Google o a uno strumento di ricerca AI. Con l'opzione disattivata, Raffa può comunque proporre una ricerca web per una domanda di procurement e chiede prima il permesso. Si cercano solo le parole della domanda -- nulla dei tuoi contratti esce da Raffa; i risultati web sono indicati come non verificati.",
  "Only a Workspace Admin can change this.": "Solo un Admin del workspace può cambiare questa impostazione.",
  "The setting could not be saved.": "Non è stato possibile salvare l'impostazione.",

  // Guida Raffa
  "Invite a colleague with their work email and choose their role: Procurement or Workspace Admin.":
    "Invita un collega con la sua email di lavoro e scegli il ruolo: Procurement o Admin del workspace.",
  "See who works in this workspace. To invite someone, ask a Workspace Admin.":
    "Guarda chi lavora in questo workspace. Per invitare qualcuno, chiedi a un Admin del workspace.",
  "Procurement asks Raffa, uploads and reviews documents and handles renewals; a Workspace Admin can also delete documents and manage members. Everyone works on the same contracts; chats stay personal.":
    "Procurement fa domande a Raffa, carica e verifica i documenti e gestisce i rinnovi; un Admin del workspace può anche eliminare documenti e gestire i membri. Tutti lavorano sugli stessi contratti; le chat restano personali.",
  "The colleague gets a link that works once and expires. Until they accept it they show as Invited, and you can send a new invitation or revoke it.":
    "Il collega riceve un link che funziona una sola volta e scade. Finché non lo accetta il suo stato è Invitato, e puoi inviare un nuovo invito o revocarlo.",
  "Once a Workspace Admin invites someone, they show here as Invited until they accept.":
    "Quando un Admin del workspace invita qualcuno, qui il suo stato è Invitato finché non accetta.",

  // Tips (`components/infoTipCopy.ts`: SCREEN_GUIDES.members, TIPS.membersRole/Status/Invite)
  "How members work": "Come funzionano i membri",
  "Invite your colleagues with their work email. Everyone in the workspace works on the same documents and contracts; chats stay personal.":
    "Invita i colleghi con la loro email di lavoro. Tutti nel workspace lavorano sugli stessi documenti e contratti; le chat restano personali.",
  "The table shows who has joined and who is still invited. From it you can revoke an invitation, issue a new one, or remove someone.":
    "La tabella mostra chi è entrato e chi è ancora invitato. Da qui puoi revocare un invito, inviarne uno nuovo o rimuovere qualcuno.",
  "What the roles can do": "Cosa possono fare i ruoli",
  "Procurement: asks Raffa, uploads and reviews documents, handles renewals.":
    "Procurement: fa domande a Raffa, carica e verifica documenti, gestisce i rinnovi.",
  "Workspace Admin: all of that, plus deleting documents and managing members.":
    "Admin del workspace: tutto questo, più eliminare documenti e gestire i membri.",
  "What the statuses mean": "Cosa significano gli stati",
  "Invited: the invitation is out, not accepted yet. Active: the person has joined. Expired: the invitation ran out before it was used.":
    "Invitato: l'invito è partito ma non è ancora stato accettato. Attivo: la persona è entrata. Scaduto: l'invito è scaduto prima di essere usato.",
  "How invitations work": "Come funzionano gli inviti",
  "The person gets a link by email to join this workspace. If the email cannot go out, copy the link and share it yourself.":
    "La persona riceve via email un link per entrare in questo workspace. Se l'email non può partire, copia il link e condividilo tu.",
  "The link works once and expires. Until it is used, the person shows as Invited, and you can issue a new invitation or revoke it.":
    "Il link funziona una sola volta e scade. Finché non viene usato, lo stato resta Invitato e puoi inviare un nuovo invito o revocarlo.",
};
