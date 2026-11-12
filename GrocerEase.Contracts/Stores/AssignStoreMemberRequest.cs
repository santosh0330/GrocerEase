namespace GrocerEase.Contracts.Stores;

/// <summary>Body of <c>POST /stores/{storeId}/members</c>: the email of the customer to assign.</summary>
public sealed record AssignStoreMemberRequest(string Email);
